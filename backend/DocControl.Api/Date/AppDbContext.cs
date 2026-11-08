using System.Security.Cryptography;
using System.Text;
using DocControl.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DocControl.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Documento> Documentos => Set<Documento>();
    public DbSet<VersionDocumento> Versiones => Set<VersionDocumento>();
    public DbSet<InfoBloqueo> Bloqueos => Set<InfoBloqueo>();
    public DbSet<RegistroBitacora> Bitacora => Set<RegistroBitacora>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQL Server devuelve DateTime con Kind=Unspecified; forzamos UTC para que el JSON
        // salga con "Z" y el navegador lo interprete correctamente (igual que antes en memoria).
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Documento>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).ValueGeneratedNever(); // Ids sembrados explícitos
            e.Property(d => d.Codigo).IsRequired().HasMaxLength(30);
            e.HasIndex(d => d.Codigo).IsUnique();
            e.Property(d => d.Nombre).IsRequired().HasMaxLength(200);
            e.Property(d => d.Departamento).IsRequired().HasMaxLength(100);
            e.Property(d => d.VersionActual).IsRequired().HasMaxLength(20);
            e.Property(d => d.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.Estado).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.EstadoRevision).HasConversion<string>().HasMaxLength(20)
                .HasDefaultValue(EstadoRevision.Aprobado);
            e.Property(d => d.RevisadoPor).HasMaxLength(100);
            e.Property(d => d.ComentarioRevision).HasMaxLength(1000);
            e.Property(d => d.RowVersion).IsRowVersion();

            e.HasMany(d => d.HistorialVersiones)
                .WithOne()
                .HasForeignKey(v => v.DocumentoId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(d => d.Bloqueo)
                .WithOne()
                .HasForeignKey<InfoBloqueo>(b => b.DocumentoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VersionDocumento>(e =>
        {
            e.HasKey(v => v.Id);
            e.Property(v => v.VersionTexto).IsRequired().HasMaxLength(20);
            e.Property(v => v.NombreArchivo).IsRequired().HasMaxLength(260);
            e.Property(v => v.Autor).IsRequired().HasMaxLength(100);
            e.Property(v => v.Comentario).IsRequired().HasMaxLength(1000);
            e.Property(v => v.HashSHA256).IsRequired().HasMaxLength(64);
            e.Property(v => v.RutaArchivo).HasMaxLength(400); // nullable: versiones sembradas no tienen binario
            e.Ignore(v => v.TieneArchivo); // propiedad calculada
        });

        modelBuilder.Entity<InfoBloqueo>(e =>
        {
            e.HasKey(b => b.Id);
            e.HasIndex(b => b.DocumentoId).IsUnique(); // un solo bloqueo por documento
            e.Property(b => b.UsuarioBloqueador).IsRequired().HasMaxLength(100);
            e.Property(b => b.RolBloqueador).HasConversion<string>().HasMaxLength(20);
            e.Ignore(b => b.EstaExpirado); // propiedad calculada
        });

        modelBuilder.Entity<RegistroBitacora>(e =>
        {
            e.HasKey(r => r.Folio);
            e.Property(r => r.Folio).ValueGeneratedOnAdd();
            e.Property(r => r.Usuario).IsRequired().HasMaxLength(100);
            e.Property(r => r.Rol).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.CodigoDocumento).IsRequired().HasMaxLength(30);
            e.Property(r => r.NombreDocumento).IsRequired().HasMaxLength(200);
            e.Property(r => r.Version).IsRequired().HasMaxLength(20);
            e.Property(r => r.Accion).HasConversion<string>().HasMaxLength(30);
            e.Property(r => r.Observaciones).IsRequired().HasMaxLength(2000);
            e.HasIndex(r => r.FechaHora);
        });

        // ── Data Seeding ──
        var seed = SeedData.Crear();
        modelBuilder.Entity<Documento>().HasData(seed.Documentos);
        modelBuilder.Entity<VersionDocumento>().HasData(seed.Versiones);
    }
}

/// <summary>Fuerza Kind=Utc al leer y normaliza a UTC al escribir.</summary>
public class UtcDateTimeConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}

/// <summary>Datos de prueba iniciales (fechas fijas para que la migración sea determinista).</summary>
internal static class SeedData
{
    private static readonly DateTime Base = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private record V(string Version, string Autor, string Comentario, int DiasAtras, long Bytes);

    public static (object[] Documentos, object[] Versiones) Crear()
    {
        var docs = new List<object>();
        var vers = new List<object>();
        int versionId = 1;

        void Agregar(int id, string codigo, string nombre, TipoDocumento tipo, string depto, int creadoHaceDias, params V[] historial)
        {
            string ext = tipo switch
            {
                TipoDocumento.Word => "docx",
                TipoDocumento.Excel => "xlsx",
                TipoDocumento.PowerPoint => "pptx",
                _ => "pdf"
            };
            var ultima = historial[^1];

            docs.Add(new
            {
                Id = id,
                Codigo = codigo,
                Nombre = nombre,
                Tipo = tipo,
                Departamento = depto,
                VersionActual = ultima.Version,
                NumeroVersionActual = historial.Length,
                Estado = EstadoDocumento.Disponible,
                EstadoRevision = EstadoRevision.Aprobado,
                FechaCreacion = Base.AddDays(-creadoHaceDias),
                FechaUltimaModificacion = Base.AddDays(-ultima.DiasAtras)
            });

            for (int i = 0; i < historial.Length; i++)
            {
                var v = historial[i];
                vers.Add(new
                {
                    Id = versionId++,
                    DocumentoId = id,
                    NumeroVersion = i + 1,
                    VersionTexto = v.Version,
                    NombreArchivo = $"{codigo}_{v.Version}.{ext}",
                    Autor = v.Autor,
                    Comentario = v.Comentario,
                    HashSHA256 = Hash($"{codigo}|{v.Version}|{v.Comentario}"),
                    FechaCreacion = Base.AddDays(-v.DiasAtras),
                    TamanoBytes = v.Bytes
                });
            }
        }

        Agregar(1, "POL-RH-001", "Política de Seguridad e Higiene", TipoDocumento.Word, "Recursos Humanos", 120,
            new V("v1.0", "Lic. María López", "Creación inicial del documento", 120, 245000),
            new V("v1.1", "Lic. María López", "Correcciones de formato y ortografía", 90, 248000),
            new V("v2.0", "Carlos Ruiz", "Actualización mayor: nuevas normas NOM-035", 30, 312000),
            new V("v2.1", "Carlos Ruiz", "Ajuste en sección 4.2 - Protocolo de emergencia", 5, 318000));

        Agregar(2, "FIN-PRE-002", "Presupuesto Anual 2026", TipoDocumento.Excel, "Finanzas", 60,
            new V("v1.0", "C.P. Laura Méndez", "Presupuesto base aprobado por dirección", 60, 1520000),
            new V("v1.1", "C.P. Laura Méndez", "Ajuste partida de tecnología Q3", 30, 1580000),
            new V("v1.2", "Carlos Ruiz", "Incorporación de proyección de gastos operativos", 2, 1640000));

        Agregar(3, "DIR-EST-003", "Plan Estratégico 2026-2028", TipoDocumento.PowerPoint, "Dirección General", 15,
            new V("v1.0", "Ing. Roberto Díaz", "Presentación inicial aprobada por comité", 15, 4200000));

        Agregar(4, "LEG-CON-004", "Contrato Marco Proveedores", TipoDocumento.PDF, "Legal", 200,
            new V("v1.0", "Lic. Fernanda Torres", "Contrato base redactado por jurídico", 200, 890000),
            new V("v1.1", "Lic. Fernanda Torres", "Adición de cláusula de confidencialidad", 150, 920000),
            new V("v2.0", "Lic. Fernanda Torres", "Reestructuración completa - nuevo marco legal", 80, 1100000),
            new V("v2.1", "Carlos Ruiz", "Actualización de montos y tarifas", 40, 1120000),
            new V("v3.0", "Ing. Roberto Díaz", "Versión definitiva con firma electrónica", 10, 1250000));

        Agregar(5, "TI-MAN-005", "Manual de Operaciones TI", TipoDocumento.Word, "Tecnología", 45,
            new V("v1.0", "Ing. Roberto Díaz", "Manual inicial de procedimientos de TI", 45, 520000),
            new V("v1.1", "Carlos Ruiz", "Agregado procedimiento de respaldos y DR", 8, 580000));

        Agregar(6, "CAL-AUD-006", "Informe de Auditoría Interna Q3", TipoDocumento.PDF, "Calidad", 7,
            new V("v1.0", "Ana Gómez", "Informe de hallazgos de auditoría Q3-2026", 7, 2100000));

        return (docs.ToArray(), vers.ToArray());
    }

    private static string Hash(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
