using System.Security.Cryptography;
using System.Text;
using DocControl.Api.Models;

namespace DocControl.Api.Services;

/// <summary>
/// Servicio Singleton que mantiene el estado completo del workspace documental en memoria.
/// Simula una base de datos con datos de prueba iniciales para el prototipo.
/// </summary>
public class DocumentWorkspaceService
{
    private readonly List<Documento> _documentos;
    private readonly List<RegistroBitacora> _bitacora;
    private readonly object _lock = new();
    private int _siguienteFolio = 1;

    public DocumentWorkspaceService()
    {
        _bitacora = new List<RegistroBitacora>();
        _documentos = GenerarDatosPrueba();
    }

    // ═══════════════════════════════════════════════════════════
    //  CONSULTAS
    // ═══════════════════════════════════════════════════════════

    public List<DocumentoResumen> ObtenerTodosLosDocumentos()
    {
        lock (_lock)
        {
            // Limpiar bloqueos expirados automáticamente
            LimpiarBloqueosExpirados();
            return _documentos.Select(MapearAResumen).ToList();
        }
    }

    public Documento? ObtenerDocumentoPorId(int id)
    {
        lock (_lock)
        {
            LimpiarBloqueosExpirados();
            return _documentos.FirstOrDefault(d => d.Id == id);
        }
    }

    public List<VersionDocumento> ObtenerHistorial(int id)
    {
        lock (_lock)
        {
            var doc = _documentos.FirstOrDefault(d => d.Id == id);
            return doc?.HistorialVersiones.OrderByDescending(v => v.NumeroVersion).ToList()
                   ?? new List<VersionDocumento>();
        }
    }

    public ResumenKpis ObtenerKpis()
    {
        lock (_lock)
        {
            LimpiarBloqueosExpirados();
            return new ResumenKpis
            {
                TotalDocumentos = _documentos.Count,
                DocumentosDisponibles = _documentos.Count(d => d.Estado == EstadoDocumento.Disponible),
                DocumentosBloqueados = _documentos.Count(d => d.Estado == EstadoDocumento.Bloqueado),
                RegistrosBitacora = _bitacora.Count
            };
        }
    }

    public List<RegistroBitacora> ObtenerBitacora()
    {
        lock (_lock)
        {
            return _bitacora.OrderByDescending(b => b.FechaHora).ToList();
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  BLOQUEO (CHECK-OUT)
    // ═══════════════════════════════════════════════════════════

    public (bool exito, string mensaje) BloquearDocumento(int id, string usuario, RolUsuario rol)
    {
        lock (_lock)
        {
            LimpiarBloqueosExpirados();

            if (rol == RolUsuario.Lector)
                return (false, "El rol 'Lector' no tiene permisos para bloquear documentos.");

            var doc = _documentos.FirstOrDefault(d => d.Id == id);
            if (doc == null)
                return (false, $"Documento con Id {id} no encontrado.");

            if (doc.Estado == EstadoDocumento.Bloqueado && doc.Bloqueo != null && !doc.Bloqueo.EstaExpirado)
                return (false, $"CONFLICTO: El documento ya está bloqueado por '{doc.Bloqueo.UsuarioBloqueador}' hasta las {doc.Bloqueo.FechaExpiracion:HH:mm:ss dd/MM/yyyy}.");

            doc.Estado = EstadoDocumento.Bloqueado;
            doc.Bloqueo = new InfoBloqueo
            {
                UsuarioBloqueador = usuario,
                RolBloqueador = rol,
                FechaBloqueo = DateTime.UtcNow,
                FechaExpiracion = DateTime.UtcNow.AddMinutes(30)
            };

            RegistrarEnBitacora(usuario, rol, doc, AccionBitacora.BLOQUEO_EDICION,
                $"Bloqueo de edición asignado por 30 minutos. Expira: {doc.Bloqueo.FechaExpiracion:HH:mm:ss}");

            return (true, $"Documento bloqueado exitosamente hasta las {doc.Bloqueo.FechaExpiracion:HH:mm:ss}.");
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  EXTENSIÓN DE TIEMPO DE BLOQUEO
    // ═══════════════════════════════════════════════════════════

    public (bool exito, string mensaje) ExtenderBloqueo(int id, string usuario, RolUsuario rol)
    {
        lock (_lock)
        {
            if (rol == RolUsuario.Lector)
                return (false, "El rol 'Lector' no tiene permisos para extender bloqueos.");

            var doc = _documentos.FirstOrDefault(d => d.Id == id);
            if (doc == null)
                return (false, $"Documento con Id {id} no encontrado.");

            if (doc.Estado != EstadoDocumento.Bloqueado || doc.Bloqueo == null)
                return (false, "El documento no está bloqueado actualmente.");

            if (doc.Bloqueo.EstaExpirado)
            {
                doc.Estado = EstadoDocumento.Disponible;
                doc.Bloqueo = null;
                return (false, "El bloqueo ha expirado. Debe solicitar un nuevo bloqueo.");
            }

            if (doc.Bloqueo.UsuarioBloqueador != usuario)
                return (false, $"Solo el usuario '{doc.Bloqueo.UsuarioBloqueador}' (dueño del bloqueo) puede extender el tiempo.");

            doc.Bloqueo.FechaExpiracion = doc.Bloqueo.FechaExpiracion.AddMinutes(15);

            RegistrarEnBitacora(usuario, rol, doc, AccionBitacora.EXTENSION_TIEMPO,
                $"Tiempo de bloqueo extendido +15 min. Nueva expiración: {doc.Bloqueo.FechaExpiracion:HH:mm:ss}");

            return (true, $"Tiempo extendido exitosamente. Nueva expiración: {doc.Bloqueo.FechaExpiracion:HH:mm:ss}.");
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  CHECK-IN (SUBIR NUEVA VERSIÓN)
    // ═══════════════════════════════════════════════════════════

    public (bool exito, string mensaje) Checkin(int id, string usuario, RolUsuario rol,
        string nombreArchivo, string comentario, bool versionMayor)
    {
        lock (_lock)
        {
            if (rol == RolUsuario.Lector)
                return (false, "El rol 'Lector' no tiene permisos para subir versiones.");

            if (string.IsNullOrWhiteSpace(comentario))
                return (false, "El comentario de cambios es obligatorio.");

            if (string.IsNullOrWhiteSpace(nombreArchivo))
                return (false, "El nombre del archivo es obligatorio.");

            var doc = _documentos.FirstOrDefault(d => d.Id == id);
            if (doc == null)
                return (false, $"Documento con Id {id} no encontrado.");

            if (doc.Estado != EstadoDocumento.Bloqueado || doc.Bloqueo == null)
                return (false, "El documento no está bloqueado. Debe hacer check-out primero.");

            if (doc.Bloqueo.EstaExpirado)
            {
                doc.Estado = EstadoDocumento.Disponible;
                doc.Bloqueo = null;
                return (false, "El bloqueo ha expirado. El check-in ya no es válido.");
            }

            if (doc.Bloqueo.UsuarioBloqueador != usuario)
                return (false, $"Solo el usuario '{doc.Bloqueo.UsuarioBloqueador}' (dueño del bloqueo) puede hacer check-in.");

            // Calcular nueva versión
            int versionMajor = doc.NumeroVersionActual;
            int versionMinor = 0;
            var partes = doc.VersionActual.Replace("v", "").Split('.');
            if (partes.Length == 2)
            {
                int.TryParse(partes[0], out versionMajor);
                int.TryParse(partes[1], out versionMinor);
            }

            if (versionMayor)
            {
                versionMajor++;
                versionMinor = 0;
            }
            else
            {
                versionMinor++;
            }

            string nuevaVersion = $"v{versionMajor}.{versionMinor}";

            // Generar hash SHA-256 simulado
            string hashInput = $"{doc.Codigo}-{nuevaVersion}-{nombreArchivo}-{DateTime.UtcNow.Ticks}-{comentario}";
            string hash = GenerarSHA256(hashInput);

            // Crear registro de versión
            var version = new VersionDocumento
            {
                NumeroVersion = doc.HistorialVersiones.Count + 1,
                VersionTexto = nuevaVersion,
                NombreArchivo = nombreArchivo,
                Autor = usuario,
                Comentario = comentario,
                HashSHA256 = hash,
                FechaCreacion = DateTime.UtcNow,
                TamanoBytes = new Random().Next(50000, 5000000) // Simulado
            };

            doc.HistorialVersiones.Add(version);
            doc.VersionActual = nuevaVersion;
            doc.NumeroVersionActual = doc.HistorialVersiones.Count;
            doc.FechaUltimaModificacion = DateTime.UtcNow;

            // Liberar bloqueo automáticamente
            doc.Estado = EstadoDocumento.Disponible;
            doc.Bloqueo = null;

            RegistrarEnBitacora(usuario, rol, doc, AccionBitacora.NUEVA_VERSION,
                $"Nueva versión {nuevaVersion} subida. Archivo: {nombreArchivo}. Comentario: {comentario}. Hash: {hash[..16]}...");

            return (true, $"Check-in exitoso. Nueva versión: {nuevaVersion}. Hash: {hash[..16]}...");
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  DESBLOQUEO FORZADO (ADMIN)
    // ═══════════════════════════════════════════════════════════

    public (bool exito, string mensaje) DesbloqueoForzado(int id, string usuario, RolUsuario rol, string motivo)
    {
        lock (_lock)
        {
            if (rol != RolUsuario.Administrador)
                return (false, "Solo un Administrador puede realizar un desbloqueo forzado.");

            if (string.IsNullOrWhiteSpace(motivo))
                return (false, "El motivo del desbloqueo forzado es obligatorio.");

            var doc = _documentos.FirstOrDefault(d => d.Id == id);
            if (doc == null)
                return (false, $"Documento con Id {id} no encontrado.");

            if (doc.Estado != EstadoDocumento.Bloqueado || doc.Bloqueo == null)
                return (false, "El documento no está bloqueado actualmente.");

            string usuarioAnterior = doc.Bloqueo.UsuarioBloqueador;
            doc.Estado = EstadoDocumento.Disponible;
            doc.Bloqueo = null;

            RegistrarEnBitacora(usuario, rol, doc, AccionBitacora.DESBLOQUEO_FORZADO,
                $"Desbloqueo forzado ejecutado. Usuario afectado: {usuarioAnterior}. Motivo: {motivo}");

            return (true, $"Desbloqueo forzado exitoso. Se liberó el bloqueo de '{usuarioAnterior}'.");
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  MÉTODOS PRIVADOS
    // ═══════════════════════════════════════════════════════════

    private void LimpiarBloqueosExpirados()
    {
        foreach (var doc in _documentos.Where(d => d.Estado == EstadoDocumento.Bloqueado && d.Bloqueo != null))
        {
            if (doc.Bloqueo!.EstaExpirado)
            {
                doc.Estado = EstadoDocumento.Disponible;
                doc.Bloqueo = null;
            }
        }
    }

    private void RegistrarEnBitacora(string usuario, RolUsuario rol, Documento doc,
        AccionBitacora accion, string observaciones)
    {
        _bitacora.Add(new RegistroBitacora
        {
            Folio = _siguienteFolio++,
            FechaHora = DateTime.UtcNow,
            Usuario = usuario,
            Rol = rol,
            CodigoDocumento = doc.Codigo,
            NombreDocumento = doc.Nombre,
            Version = doc.VersionActual,
            Accion = accion,
            Observaciones = observaciones
        });
    }

    private static string GenerarSHA256(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder();
        foreach (byte b in bytes)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private DocumentoResumen MapearAResumen(Documento doc)
    {
        return new DocumentoResumen
        {
            Id = doc.Id,
            Codigo = doc.Codigo,
            Nombre = doc.Nombre,
            Tipo = doc.Tipo.ToString(),
            Departamento = doc.Departamento,
            VersionActual = doc.VersionActual,
            Estado = doc.Estado.ToString(),
            Bloqueo = doc.Bloqueo != null ? new BloqueoResumen
            {
                Usuario = doc.Bloqueo.UsuarioBloqueador,
                Rol = doc.Bloqueo.RolBloqueador.ToString(),
                FechaBloqueo = doc.Bloqueo.FechaBloqueo,
                FechaExpiracion = doc.Bloqueo.FechaExpiracion,
                Expirado = doc.Bloqueo.EstaExpirado
            } : null,
            FechaCreacion = doc.FechaCreacion,
            FechaUltimaModificacion = doc.FechaUltimaModificacion
        };
    }

    private List<Documento> GenerarDatosPrueba()
    {
        var ahora = DateTime.UtcNow;
        return new List<Documento>
        {
            new()
            {
                Id = 1,
                Codigo = "POL-RH-001",
                Nombre = "Política de Seguridad e Higiene",
                Tipo = TipoDocumento.Word,
                Departamento = "Recursos Humanos",
                VersionActual = "v2.1",
                NumeroVersionActual = 4,
                Estado = EstadoDocumento.Disponible,
                FechaCreacion = ahora.AddDays(-120),
                FechaUltimaModificacion = ahora.AddDays(-5),
                HistorialVersiones = new List<VersionDocumento>
                {
                    new() { NumeroVersion = 1, VersionTexto = "v1.0", NombreArchivo = "POL-RH-001_v1.0.docx", Autor = "Lic. María López", Comentario = "Creación inicial del documento", HashSHA256 = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2", FechaCreacion = ahora.AddDays(-120), TamanoBytes = 245000 },
                    new() { NumeroVersion = 2, VersionTexto = "v1.1", NombreArchivo = "POL-RH-001_v1.1.docx", Autor = "Lic. María López", Comentario = "Correcciones de formato y ortografía", HashSHA256 = "b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3", FechaCreacion = ahora.AddDays(-90), TamanoBytes = 248000 },
                    new() { NumeroVersion = 3, VersionTexto = "v2.0", NombreArchivo = "POL-RH-001_v2.0.docx", Autor = "Carlos Ruiz", Comentario = "Actualización mayor: nuevas normas NOM-035", HashSHA256 = "c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4", FechaCreacion = ahora.AddDays(-30), TamanoBytes = 312000 },
                    new() { NumeroVersion = 4, VersionTexto = "v2.1", NombreArchivo = "POL-RH-001_v2.1.docx", Autor = "Carlos Ruiz", Comentario = "Ajuste en sección 4.2 - Protocolo de emergencia", HashSHA256 = "d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5", FechaCreacion = ahora.AddDays(-5), TamanoBytes = 318000 }
                }
            },
            new()
            {
                Id = 2,
                Codigo = "FIN-PRE-002",
                Nombre = "Presupuesto Anual 2026",
                Tipo = TipoDocumento.Excel,
                Departamento = "Finanzas",
                VersionActual = "v1.2",
                NumeroVersionActual = 3,
                Estado = EstadoDocumento.Disponible,
                FechaCreacion = ahora.AddDays(-60),
                FechaUltimaModificacion = ahora.AddDays(-2),
                HistorialVersiones = new List<VersionDocumento>
                {
                    new() { NumeroVersion = 1, VersionTexto = "v1.0", NombreArchivo = "FIN-PRE-002_v1.0.xlsx", Autor = "C.P. Laura Méndez", Comentario = "Presupuesto base aprobado por dirección", HashSHA256 = "e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6", FechaCreacion = ahora.AddDays(-60), TamanoBytes = 1520000 },
                    new() { NumeroVersion = 2, VersionTexto = "v1.1", NombreArchivo = "FIN-PRE-002_v1.1.xlsx", Autor = "C.P. Laura Méndez", Comentario = "Ajuste partida de tecnología Q3", HashSHA256 = "f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1", FechaCreacion = ahora.AddDays(-30), TamanoBytes = 1580000 },
                    new() { NumeroVersion = 3, VersionTexto = "v1.2", NombreArchivo = "FIN-PRE-002_v1.2.xlsx", Autor = "Carlos Ruiz", Comentario = "Incorporación de proyección de gastos operativos", HashSHA256 = "a2b3c4d5e6f7a2b3c4d5e6f7a2b3c4d5e6f7a2b3c4d5e6f7a2b3c4d5e6f7a2b3", FechaCreacion = ahora.AddDays(-2), TamanoBytes = 1640000 }
                }
            },
            new()
            {
                Id = 3,
                Codigo = "DIR-EST-003",
                Nombre = "Plan Estratégico 2026-2028",
                Tipo = TipoDocumento.PowerPoint,
                Departamento = "Dirección General",
                VersionActual = "v1.0",
                NumeroVersionActual = 1,
                Estado = EstadoDocumento.Disponible,
                FechaCreacion = ahora.AddDays(-15),
                FechaUltimaModificacion = ahora.AddDays(-15),
                HistorialVersiones = new List<VersionDocumento>
                {
                    new() { NumeroVersion = 1, VersionTexto = "v1.0", NombreArchivo = "DIR-EST-003_v1.0.pptx", Autor = "Ing. Roberto Díaz", Comentario = "Presentación inicial aprobada por comité", HashSHA256 = "b3c4d5e6f7a1b3c4d5e6f7a1b3c4d5e6f7a1b3c4d5e6f7a1b3c4d5e6f7a1b3c4", FechaCreacion = ahora.AddDays(-15), TamanoBytes = 4200000 }
                }
            },
            new()
            {
                Id = 4,
                Codigo = "LEG-CON-004",
                Nombre = "Contrato Marco Proveedores",
                Tipo = TipoDocumento.PDF,
                Departamento = "Legal",
                VersionActual = "v3.0",
                NumeroVersionActual = 5,
                Estado = EstadoDocumento.Disponible,
                FechaCreacion = ahora.AddDays(-200),
                FechaUltimaModificacion = ahora.AddDays(-10),
                HistorialVersiones = new List<VersionDocumento>
                {
                    new() { NumeroVersion = 1, VersionTexto = "v1.0", NombreArchivo = "LEG-CON-004_v1.0.pdf", Autor = "Lic. Fernanda Torres", Comentario = "Contrato base redactado por jurídico", HashSHA256 = "c4d5e6f7a1b2c4d5e6f7a1b2c4d5e6f7a1b2c4d5e6f7a1b2c4d5e6f7a1b2c4d5", FechaCreacion = ahora.AddDays(-200), TamanoBytes = 890000 },
                    new() { NumeroVersion = 2, VersionTexto = "v1.1", NombreArchivo = "LEG-CON-004_v1.1.pdf", Autor = "Lic. Fernanda Torres", Comentario = "Adición de cláusula de confidencialidad", HashSHA256 = "d5e6f7a1b2c3d5e6f7a1b2c3d5e6f7a1b2c3d5e6f7a1b2c3d5e6f7a1b2c3d5e6", FechaCreacion = ahora.AddDays(-150), TamanoBytes = 920000 },
                    new() { NumeroVersion = 3, VersionTexto = "v2.0", NombreArchivo = "LEG-CON-004_v2.0.pdf", Autor = "Lic. Fernanda Torres", Comentario = "Reestructuración completa - nuevo marco legal", HashSHA256 = "e6f7a1b2c3d4e6f7a1b2c3d4e6f7a1b2c3d4e6f7a1b2c3d4e6f7a1b2c3d4e6f7", FechaCreacion = ahora.AddDays(-80), TamanoBytes = 1100000 },
                    new() { NumeroVersion = 4, VersionTexto = "v2.1", NombreArchivo = "LEG-CON-004_v2.1.pdf", Autor = "Carlos Ruiz", Comentario = "Actualización de montos y tarifas", HashSHA256 = "f7a1b2c3d4e5f7a1b2c3d4e5f7a1b2c3d4e5f7a1b2c3d4e5f7a1b2c3d4e5f7a1", FechaCreacion = ahora.AddDays(-40), TamanoBytes = 1120000 },
                    new() { NumeroVersion = 5, VersionTexto = "v3.0", NombreArchivo = "LEG-CON-004_v3.0.pdf", Autor = "Ing. Roberto Díaz", Comentario = "Versión definitiva con firma electrónica", HashSHA256 = "a8b9c0d1e2f3a8b9c0d1e2f3a8b9c0d1e2f3a8b9c0d1e2f3a8b9c0d1e2f3a8b9", FechaCreacion = ahora.AddDays(-10), TamanoBytes = 1250000 }
                }
            },
            new()
            {
                Id = 5,
                Codigo = "TI-MAN-005",
                Nombre = "Manual de Operaciones TI",
                Tipo = TipoDocumento.Word,
                Departamento = "Tecnología",
                VersionActual = "v1.1",
                NumeroVersionActual = 2,
                Estado = EstadoDocumento.Disponible,
                FechaCreacion = ahora.AddDays(-45),
                FechaUltimaModificacion = ahora.AddDays(-8),
                HistorialVersiones = new List<VersionDocumento>
                {
                    new() { NumeroVersion = 1, VersionTexto = "v1.0", NombreArchivo = "TI-MAN-005_v1.0.docx", Autor = "Ing. Roberto Díaz", Comentario = "Manual inicial de procedimientos de TI", HashSHA256 = "1a2b3c4d5e6f1a2b3c4d5e6f1a2b3c4d5e6f1a2b3c4d5e6f1a2b3c4d5e6f1a2b", FechaCreacion = ahora.AddDays(-45), TamanoBytes = 520000 },
                    new() { NumeroVersion = 2, VersionTexto = "v1.1", NombreArchivo = "TI-MAN-005_v1.1.docx", Autor = "Carlos Ruiz", Comentario = "Agregado procedimiento de respaldos y DR", HashSHA256 = "2b3c4d5e6f1a2b3c4d5e6f1a2b3c4d5e6f1a2b3c4d5e6f1a2b3c4d5e6f1a2b3c", FechaCreacion = ahora.AddDays(-8), TamanoBytes = 580000 }
                }
            },
            new()
            {
                Id = 6,
                Codigo = "CAL-AUD-006",
                Nombre = "Informe de Auditoría Interna Q3",
                Tipo = TipoDocumento.PDF,
                Departamento = "Calidad",
                VersionActual = "v1.0",
                NumeroVersionActual = 1,
                Estado = EstadoDocumento.Disponible,
                FechaCreacion = ahora.AddDays(-7),
                FechaUltimaModificacion = ahora.AddDays(-7),
                HistorialVersiones = new List<VersionDocumento>
                {
                    new() { NumeroVersion = 1, VersionTexto = "v1.0", NombreArchivo = "CAL-AUD-006_v1.0.pdf", Autor = "Ana Gómez", Comentario = "Informe de hallazgos de auditoría Q3-2026", HashSHA256 = "3c4d5e6f1a2b3c4d5e6f1a2b3c4d5e6f1a2b3c4d5e6f1a2b3c4d5e6f1a2b3c4d", FechaCreacion = ahora.AddDays(-7), TamanoBytes = 2100000 }
                }
            }
        };
    }
}

