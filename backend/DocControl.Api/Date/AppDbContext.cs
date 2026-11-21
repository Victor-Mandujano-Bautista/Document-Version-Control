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
    public DbSet<Carpeta> Carpetas => Set<Carpeta>();
    public DbSet<ComentarioDocumento> Comentarios => Set<ComentarioDocumento>();
    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();
    public DbSet<SuscripcionLiberacion> SuscripcionesLiberacion => Set<SuscripcionLiberacion>();
    public DbSet<EnlaceCompartido> EnlacesCompartidos => Set<EnlaceCompartido>();

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
            e.Property(d => d.Etiquetas).IsRequired().HasMaxLength(500);
            e.Property(d => d.FechaEliminacion).HasConversion<UtcDateTimeConverter>();
            e.Property(d => d.EliminadoPor).HasMaxLength(100);
            e.HasIndex(d => d.Eliminado); // filtro frecuente activos / papelera
            e.HasIndex(d => d.CarpetaId);

            e.HasMany(d => d.HistorialVersiones)
                .WithOne()
                .HasForeignKey(v => v.DocumentoId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(d => d.Bloqueo)
                .WithOne()
                .HasForeignKey<InfoBloqueo>(b => b.DocumentoId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(d => d.Comentarios)
                .WithOne()
                .HasForeignKey(c => c.DocumentoId)
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
            e.Property(v => v.MotivoCambio).IsRequired().HasMaxLength(1000);
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
            e.Property(r => r.DatosMaquina).IsRequired().HasMaxLength(500);
            e.HasIndex(r => r.FechaHora);
        });

        modelBuilder.Entity<Carpeta>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Nombre).IsRequired().HasMaxLength(100);
            e.HasIndex(c => new { c.Nombre, c.CarpetaPadreId }).IsUnique();
            e.Property(c => c.CreadoPor).IsRequired().HasMaxLength(100);
            e.Property(c => c.Color).IsRequired().HasMaxLength(20);
        });

        modelBuilder.Entity<ComentarioDocumento>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.VersionNumero).IsRequired().HasMaxLength(30);
            e.Property(c => c.Autor).IsRequired().HasMaxLength(100);
            e.Property(c => c.RolAutor).IsRequired().HasMaxLength(30);
            e.Property(c => c.Texto).IsRequired().HasMaxLength(2000);
            e.HasIndex(c => new { c.DocumentoId, c.FechaCreacion });
        });

        modelBuilder.Entity<Notificacion>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Destinatario).IsRequired().HasMaxLength(100);
            e.Property(n => n.RolDestinatario).IsRequired().HasMaxLength(30);
            e.Property(n => n.Tipo).IsRequired().HasMaxLength(40);
            e.Property(n => n.Titulo).IsRequired().HasMaxLength(200);
            e.Property(n => n.Mensaje).IsRequired().HasMaxLength(1000);
            e.Property(n => n.CodigoDocumento).HasMaxLength(30);
            e.HasIndex(n => new { n.RolDestinatario, n.Leida, n.FechaCreacion });
            e.HasOne<Documento>()
                .WithMany()
                .HasForeignKey(n => n.DocumentoId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SuscripcionLiberacion>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Usuario).IsRequired().HasMaxLength(100);
            e.Property(s => s.Rol).IsRequired().HasMaxLength(30);
            e.HasIndex(s => new { s.DocumentoId, s.Notificado });
            e.HasOne<Documento>()
                .WithMany()
                .HasForeignKey(s => s.DocumentoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EnlaceCompartido>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Token).IsRequired().HasMaxLength(100);
            e.HasIndex(l => l.Token).IsUnique();
            e.Property(l => l.CreadoPor).IsRequired().HasMaxLength(100);
            e.Property(l => l.FechaRevocacion).HasConversion<UtcDateTimeConverter>();
            e.HasOne<Documento>()
                .WithMany()
                .HasForeignKey(l => l.DocumentoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Data Seeding ──
        var seed = SeedData.Crear();
        modelBuilder.Entity<Carpeta>().HasData(seed.Carpetas);
        modelBuilder.Entity<Documento>().HasData(seed.Documentos);
        modelBuilder.Entity<VersionDocumento>().HasData(seed.Versiones);
        modelBuilder.Entity<ComentarioDocumento>().HasData(seed.Comentarios);
        modelBuilder.Entity<Notificacion>().HasData(seed.Notificaciones);
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

    /// <summary>Versión sembrada: Comentario = ¿qué se hizo?, Motivo = ¿por qué se hizo?</summary>
    private record V(string Version, string Autor, string Comentario, int DiasAtras, long Bytes, string Motivo);

    public static (object[] Carpetas, object[] Documentos, object[] Versiones, object[] Comentarios, object[] Notificaciones) Crear()
    {
        var carpetas = new object[]
        {
            new { Id = 1, Nombre = "Recursos Humanos", Color = "Rojo", CreadoPor = "Elena - Administrador", FechaCreacion = Base.AddDays(-120) },
            new { Id = 2, Nombre = "Finanzas", Color = "Verde", CreadoPor = "Elena - Administrador", FechaCreacion = Base.AddDays(-60) },
            new { Id = 3, Nombre = "Dirección General", Color = "Azul", CreadoPor = "Elena - Administrador", FechaCreacion = Base.AddDays(-15) },
            new { Id = 4, Nombre = "Legal", Color = "Morado", CreadoPor = "Roberto - Revisor", FechaCreacion = Base.AddDays(-200) },
            new { Id = 5, Nombre = "Tecnología", Color = "Cian", CreadoPor = "Roberto - Revisor", FechaCreacion = Base.AddDays(-45) },
            new { Id = 6, Nombre = "Calidad", Color = "Amarillo", CreadoPor = "Roberto - Revisor", FechaCreacion = Base.AddDays(-7) }
        };

        var docs = new List<object>();
        var vers = new List<object>();
        int versionId = 1;

        void Agregar(int id, string codigo, string nombre, TipoDocumento tipo, string depto, int creadoHaceDias,
            string etiquetas, params V[] historial)
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
                CarpetaId = id,
                VersionActual = ultima.Version,
                NumeroVersionActual = historial.Length,
                Estado = EstadoDocumento.Disponible,
                EstadoRevision = EstadoRevision.Aprobado,
                FechaCreacion = Base.AddDays(-creadoHaceDias),
                FechaUltimaModificacion = Base.AddDays(-ultima.DiasAtras),
                Eliminado = false,
                Etiquetas = etiquetas
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
                    MotivoCambio = v.Motivo,
                    HashSHA256 = Hash($"{codigo}|{v.Version}|{v.Comentario}"),
                    FechaCreacion = Base.AddDays(-v.DiasAtras),
                    TamanoBytes = v.Bytes
                });
            }
        }

        Agregar(1, "POL-RH-001", "Política de Seguridad e Higiene", TipoDocumento.Word, "Recursos Humanos", 120,
            "Oficial,Final",
            new V("v1.0", "Lic. María López", "Creación inicial del documento", 120, 245_760, "Requerimiento de cumplimiento normativo interno"),
            new V("v1.1", "Lic. María López", "Correcciones de formato y ortografía", 90, 248_832, "Observaciones de la revisión de estilo corporativo"),
            new V("v2.0", "Carlos Ruiz", "Actualización mayor: nuevas normas NOM-035", 30, 312_320, "Entrada en vigor de la NOM-035-STPS"),
            new V("v2.1", "Carlos Ruiz", "Ajuste en sección 4.2 - Protocolo de emergencia", 5, 318_464, "Hallazgo del simulacro de evacuación"));

        Agregar(2, "FIN-PRE-002", "Presupuesto Anual 2026", TipoDocumento.Excel, "Finanzas", 60,
            "Urgente,Contabilidad",
            new V("v1.0", "C.P. Laura Méndez", "Presupuesto base aprobado por dirección", 60, 1_556_480, "Cierre del ciclo de planeación anual"),
            new V("v1.1", "C.P. Laura Méndez", "Ajuste partida de tecnología Q3", 30, 1_617_920, "Solicitud de TI por renovación de licencias"),
            new V("v1.2", "Carlos Ruiz", "Incorporación de proyección de gastos operativos", 2, 1_679_360, "Petición del comité de finanzas"));

        Agregar(3, "DIR-EST-003", "Plan Estratégico 2026-2028", TipoDocumento.PowerPoint, "Dirección General", 15,
            "Urgente,Confidencial",
            new V("v1.0", "Ing. Roberto Díaz", "Presentación inicial aprobada por comité", 15, 4_300_800, "Sesión del consejo directivo"));

        Agregar(4, "LEG-CON-004", "Contrato Marco Proveedores", TipoDocumento.PDF, "Legal", 200,
            "Oficial,Final",
            new V("v1.0", "Lic. Fernanda Torres", "Contrato base redactado por jurídico", 200, 911_360, "Alta del nuevo esquema de proveedores"),
            new V("v1.1", "Lic. Fernanda Torres", "Adición de cláusula de confidencialidad", 150, 942_080, "Recomendación de auditoría legal"),
            new V("v2.0", "Lic. Fernanda Torres", "Reestructuración completa - nuevo marco legal", 80, 1_126_400, "Reforma a la legislación de adquisiciones"),
            new V("v2.1", "Carlos Ruiz", "Actualización de montos y tarifas", 40, 1_146_880, "Ajuste inflacionario anual"),
            new V("v3.0", "Ing. Roberto Díaz", "Versión definitiva con firma electrónica", 10, 1_280_000, "Formalización y firma de las partes"));

        Agregar(5, "TI-MAN-005", "Manual de Operaciones TI", TipoDocumento.Word, "Tecnología", 45,
            "En Proceso,Oficial",
            new V("v1.0", "Ing. Roberto Díaz", "Manual inicial de procedimientos de TI", 45, 532_480, "Estandarización de procesos del área"),
            new V("v1.1", "Carlos Ruiz", "Agregado procedimiento de respaldos y DR", 8, 593_920, "Plan de continuidad del negocio (DRP)"));

        Agregar(6, "CAL-AUD-006", "Informe de Auditoría Interna Q3", TipoDocumento.PDF, "Calidad", 7,
            "Urgente,Confidencial",
            new V("v1.0", "Ana Gómez", "Informe de hallazgos de auditoría Q3-2026", 7, 2_150_400, "Programa anual de auditorías ISO 9001"));

        var comentarios = new object[]
        {
            new
            {
                Id = 1,
                DocumentoId = 1,
                VersionNumero = "v2.1",
                Autor = "Elena Martínez",
                RolAutor = "Administrador",
                Texto = "@Carlos revisar el apartado 2 en la próxima actualización para incluir las nuevas rutas de evacuación.",
                FechaCreacion = Base.AddDays(-4)
            },
            new
            {
                Id = 2,
                DocumentoId = 2,
                VersionNumero = "v1.2",
                Autor = "Roberto Díaz",
                RolAutor = "Revisor",
                Texto = "@Elena las proyecciones de gastos operativos ya cuadran con el cierre contable. Listo para comité.",
                FechaCreacion = Base.AddDays(-1)
            },
            new
            {
                Id = 3,
                DocumentoId = 4,
                VersionNumero = "General",
                Autor = "Carlos Ruiz",
                RolAutor = "Editor",
                Texto = "@Roberto @Elena confirmo que la cláusula de confidencialidad quedó reflejada en la versión vigente v3.0.",
                FechaCreacion = Base.AddDays(-2)
            }
        };

        var notificaciones = new object[]
        {
            new
            {
                Id = 1,
                Destinatario = "Carlos Ruiz",
                RolDestinatario = "Editor",
                Tipo = "MENCION",
                Titulo = "Te mencionaron en POL-RH-001",
                Mensaje = "Elena Martínez te mencionó en 'Política de Seguridad e Higiene': \"@Carlos revisar el apartado 2 en la próxima actualización para incluir las nuevas rutas de evacuación.\"",
                DocumentoId = (int?)1,
                CodigoDocumento = "POL-RH-001",
                Leida = false,
                FechaCreacion = Base.AddDays(-4)
            },
            new
            {
                Id = 2,
                Destinatario = "Roberto Díaz",
                RolDestinatario = "Revisor",
                Tipo = "MENCION",
                Titulo = "Te mencionaron en LEG-CON-004",
                Mensaje = "Carlos Ruiz te mencionó en 'Contrato Marco Proveedores': \"@Roberto @Elena confirmo que la cláusula de confidencialidad quedó reflejada en la versión vigente v3.0.\"",
                DocumentoId = (int?)4,
                CodigoDocumento = "LEG-CON-004",
                Leida = false,
                FechaCreacion = Base.AddDays(-2)
            }
        };

        return (carpetas, docs.ToArray(), vers.ToArray(), comentarios, notificaciones);
    }

    private static string Hash(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
