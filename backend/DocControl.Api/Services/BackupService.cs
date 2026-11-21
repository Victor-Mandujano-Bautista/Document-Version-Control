using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocControl.Api.Data;
using DocControl.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DocControl.Api.Services;

/// <summary>
/// Motor transaccional de Respaldo y Restauración (.zip portable con SQL Server + /Storage) (RF-21).
/// </summary>
public class BackupService
{
    private static readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly string _contentRootPath;
    private readonly string _storagePath;
    private readonly string _backupsPath;

    public BackupService(AppDbContext context, IWebHostEnvironment env, IConfiguration config)
    {
        _context = context;
        _config = config;
        _contentRootPath = Path.GetFullPath(env.ContentRootPath);
        _storagePath = Path.GetFullPath(Path.Combine(_contentRootPath, "Storage"));
        string folderName = _config.GetValue<string>("BackupSettings:FolderName") ?? "Backups";
        _backupsPath = Path.GetFullPath(Path.Combine(_contentRootPath, folderName));
    }

    public string BackupsDirectoryPath => _backupsPath;

    /// <summary>
    /// Crea un respaldo completo (.zip) con manifest.json, database_snapshot.json y todos los binarios de /Storage.
    /// </summary>
    public async Task<RegistroRespaldo> CreateBackupAsync(string tipo, string usuario, string? datosMaquina = null, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        string? zipFullPath = null;
        var ahora = DateTime.UtcNow;
        string tipoNormalizado = string.Equals(tipo, "Automatico", StringComparison.OrdinalIgnoreCase) ? "Automatico" : "Manual";
        string actor = string.IsNullOrWhiteSpace(usuario) ? "Elena Martínez" : usuario.Trim();
        string nombreZip = $"DocControl_Backup_{ahora:yyyyMMdd_HHmmss}.zip";

        try
        {
            Directory.CreateDirectory(_backupsPath);
            Directory.CreateDirectory(_storagePath);

            // Evitar colisión si se generan dos respaldos en el mismo segundo
            zipFullPath = Path.Combine(_backupsPath, nombreZip);
            int sufijo = 1;
            while (File.Exists(zipFullPath))
            {
                nombreZip = $"DocControl_Backup_{ahora:yyyyMMdd_HHmmss}_{sufijo++}.zip";
                zipFullPath = Path.Combine(_backupsPath, nombreZip);
            }

            // 1. Consultar todas las tablas operacionales con AsNoTracking() hacia DTOs planos sin referencias circulares
            var snapshot = new DatabaseSnapshotDto
            {
                ConfiguracionesSistema = await _context.ConfiguracionesSistema.AsNoTracking()
                    .OrderBy(x => x.Id)
                    .Select(x => new ConfiguracionSnapshotItem
                    {
                        Id = x.Id,
                        Clave = x.Clave,
                        Valor = x.Valor,
                        ActualizadoPor = x.ActualizadoPor,
                        FechaActualizacion = x.FechaActualizacion
                    }).ToListAsync(ct),

                Carpetas = await _context.Carpetas.AsNoTracking()
                    .OrderBy(x => x.Id)
                    .Select(x => new CarpetaSnapshotItem
                    {
                        Id = x.Id,
                        Nombre = x.Nombre,
                        CreadoPor = x.CreadoPor,
                        FechaCreacion = x.FechaCreacion,
                        Color = x.Color,
                        CarpetaPadreId = x.CarpetaPadreId
                    }).ToListAsync(ct),

                PermisosCarpeta = await _context.PermisosCarpeta.AsNoTracking()
                    .OrderBy(x => x.Id)
                    .Select(x => new PermisoCarpetaSnapshotItem
                    {
                        Id = x.Id,
                        CarpetaId = x.CarpetaId,
                        Rol = x.Rol,
                        NivelAcceso = x.NivelAcceso,
                        ActualizadoPor = x.ActualizadoPor,
                        FechaActualizacion = x.FechaActualizacion
                    }).ToListAsync(ct),

                Documentos = await _context.Documentos.AsNoTracking()
                    .OrderBy(x => x.Id)
                    .Select(x => new DocumentoSnapshotItem
                    {
                        Id = x.Id,
                        Codigo = x.Codigo,
                        Nombre = x.Nombre,
                        Tipo = x.Tipo,
                        Departamento = x.Departamento,
                        CarpetaId = x.CarpetaId,
                        VersionActual = x.VersionActual,
                        NumeroVersionActual = x.NumeroVersionActual,
                        Estado = x.Estado,
                        EstadoRevision = x.EstadoRevision,
                        RevisadoPor = x.RevisadoPor,
                        ComentarioRevision = x.ComentarioRevision,
                        FechaCreacion = x.FechaCreacion,
                        FechaUltimaModificacion = x.FechaUltimaModificacion,
                        Eliminado = x.Eliminado,
                        FechaEliminacion = x.FechaEliminacion,
                        EliminadoPor = x.EliminadoPor,
                        Etiquetas = x.Etiquetas,
                        TextoIndexado = x.TextoIndexado
                    }).ToListAsync(ct),

                Bloqueos = await _context.Bloqueos.AsNoTracking()
                    .OrderBy(x => x.Id)
                    .Select(x => new InfoBloqueoSnapshotItem
                    {
                        Id = x.Id,
                        DocumentoId = x.DocumentoId,
                        UsuarioBloqueador = x.UsuarioBloqueador,
                        RolBloqueador = x.RolBloqueador,
                        FechaBloqueo = x.FechaBloqueo,
                        FechaExpiracion = x.FechaExpiracion
                    }).ToListAsync(ct),

                VersionesDocumento = await _context.Versiones.AsNoTracking()
                    .OrderBy(x => x.Id)
                    .Select(x => new VersionDocumentoSnapshotItem
                    {
                        Id = x.Id,
                        DocumentoId = x.DocumentoId,
                        NumeroVersion = x.NumeroVersion,
                        VersionTexto = x.VersionTexto,
                        NombreArchivo = x.NombreArchivo,
                        RutaArchivo = x.RutaArchivo,
                        Autor = x.Autor,
                        Comentario = x.Comentario,
                        HashSHA256 = x.HashSHA256,
                        FechaCreacion = x.FechaCreacion,
                        TamanoBytes = x.TamanoBytes,
                        MotivoCambio = x.MotivoCambio,
                        TextoIndexado = x.TextoIndexado
                    }).ToListAsync(ct),

                Comentarios = await _context.Comentarios.AsNoTracking()
                    .OrderBy(x => x.Id)
                    .Select(x => new ComentarioSnapshotItem
                    {
                        Id = x.Id,
                        DocumentoId = x.DocumentoId,
                        VersionNumero = x.VersionNumero,
                        Autor = x.Autor,
                        RolAutor = x.RolAutor,
                        Texto = x.Texto,
                        FechaCreacion = x.FechaCreacion
                    }).ToListAsync(ct),

                Notificaciones = await _context.Notificaciones.AsNoTracking()
                    .OrderBy(x => x.Id)
                    .Select(x => new NotificacionSnapshotItem
                    {
                        Id = x.Id,
                        Destinatario = x.Destinatario,
                        RolDestinatario = x.RolDestinatario,
                        Tipo = x.Tipo,
                        Titulo = x.Titulo,
                        Mensaje = x.Mensaje,
                        DocumentoId = x.DocumentoId,
                        CodigoDocumento = x.CodigoDocumento,
                        Leida = x.Leida,
                        FechaCreacion = x.FechaCreacion
                    }).ToListAsync(ct),

                SuscripcionesLiberacion = await _context.SuscripcionesLiberacion.AsNoTracking()
                    .OrderBy(x => x.Id)
                    .Select(x => new SuscripcionSnapshotItem
                    {
                        Id = x.Id,
                        DocumentoId = x.DocumentoId,
                        Usuario = x.Usuario,
                        Rol = x.Rol,
                        FechaSolicitud = x.FechaSolicitud,
                        Notificado = x.Notificado
                    }).ToListAsync(ct),

                EnlacesCompartidos = await _context.EnlacesCompartidos.AsNoTracking()
                    .OrderBy(x => x.Id)
                    .Select(x => new EnlaceCompartidoSnapshotItem
                    {
                        Id = x.Id,
                        Token = x.Token,
                        DocumentoId = x.DocumentoId,
                        VersionId = x.VersionId,
                        CreadoPor = x.CreadoPor,
                        FechaCreacion = x.FechaCreacion,
                        FechaExpiracion = x.FechaExpiracion,
                        Revocado = x.Revocado,
                        FechaRevocacion = x.FechaRevocacion,
                        PermiteDescarga = x.PermiteDescarga,
                        TotalAccesos = x.TotalAccesos
                    }).ToListAsync(ct),

                Bitacora = await _context.Bitacora.AsNoTracking()
                    .OrderBy(x => x.Folio)
                    .Select(x => new BitacoraSnapshotItem
                    {
                        Folio = x.Folio,
                        FechaHora = x.FechaHora,
                        Usuario = x.Usuario,
                        Rol = x.Rol,
                        CodigoDocumento = x.CodigoDocumento,
                        NombreDocumento = x.NombreDocumento,
                        Version = x.Version,
                        Accion = x.Accion,
                        Observaciones = x.Observaciones,
                        DatosMaquina = x.DatosMaquina
                    }).ToListAsync(ct)
            };

            byte[] snapshotBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOpts);
            string snapshotSha256 = Convert.ToHexString(SHA256.HashData(snapshotBytes)).ToLowerInvariant();

            var archivosFisicos = Directory.Exists(_storagePath)
                ? Directory.GetFiles(_storagePath, "*", SearchOption.AllDirectories)
                : Array.Empty<string>();

            var manifest = new BackupManifestDto
            {
                SchemaVersion = "1.0",
                CreatedUtc = ahora,
                CreatedBy = actor,
                Tipo = tipoNormalizado,
                SnapshotSha256 = snapshotSha256,
                TotalCarpetas = snapshot.Carpetas.Count,
                TotalDocumentos = snapshot.Documentos.Count,
                TotalVersiones = snapshot.VersionesDocumento.Count,
                TotalArchivosFisicos = archivosFisicos.Length,
                TotalRegistrosBitacora = snapshot.Bitacora.Count
            };
            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOpts);

            // 2. Empaquetar manifest.json, database_snapshot.json y Storage/* en el archivo .zip
            await using (var fs = new FileStream(zipFullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                await using (var entryStream = manifestEntry.Open())
                {
                    await entryStream.WriteAsync(manifestBytes, ct);
                }

                var snapshotEntry = archive.CreateEntry("database_snapshot.json", CompressionLevel.Optimal);
                await using (var entryStream = snapshotEntry.Open())
                {
                    await entryStream.WriteAsync(snapshotBytes, ct);
                }

                foreach (var filePath in archivosFisicos)
                {
                    string relPath = Path.GetRelativePath(_storagePath, filePath).Replace('\\', '/');
                    var storageEntry = archive.CreateEntry($"Storage/{relPath}", CompressionLevel.Optimal);
                    await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    await using var entryStream = storageEntry.Open();
                    await fileStream.CopyToAsync(entryStream, ct);
                }
            }

            // 3. Calcular SHA-256 y tamaño del .zip final
            var fileInfo = new FileInfo(zipFullPath);
            string zipSha256;
            await using (var readZip = new FileStream(zipFullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                zipSha256 = Convert.ToHexString(await SHA256.HashDataAsync(readZip, ct)).ToLowerInvariant();
            }

            var registro = new RegistroRespaldo
            {
                NombreArchivo = nombreZip,
                RutaArchivo = Path.Combine("Backups", nombreZip),
                Tipo = tipoNormalizado,
                Estado = "Completado",
                TamanoBytes = fileInfo.Length,
                TotalCarpetas = snapshot.Carpetas.Count,
                TotalDocumentos = snapshot.Documentos.Count,
                TotalVersiones = snapshot.VersionesDocumento.Count,
                TotalArchivosFisicos = archivosFisicos.Length,
                HashIntegridad = zipSha256,
                CreadoPor = actor,
                FechaCreacion = ahora
            };

            _context.RegistrosRespaldo.Add(registro);
            _context.Bitacora.Add(new RegistroBitacora
            {
                FechaHora = ahora,
                Usuario = actor,
                Rol = RolUsuario.Administrador,
                CodigoDocumento = "BACKUP",
                NombreDocumento = nombreZip,
                Version = "-",
                Accion = AccionBitacora.CREAR_RESPALDO,
                Observaciones = $"Respaldo {tipoNormalizado.ToLowerInvariant()} creado exitosamente: '{nombreZip}' ({fileInfo.Length} bytes). " +
                                $"Contenido: {registro.TotalCarpetas} carpetas, {registro.TotalDocumentos} documentos, {registro.TotalVersiones} versiones, " +
                                $"{registro.TotalArchivosFisicos} archivos físicos. SHA-256: {zipSha256[..16]}...",
                DatosMaquina = datosMaquina ?? $"Servidor: {Environment.MachineName} | Proceso: BackupService"
            });

            await _context.SaveChangesAsync(ct);

            // 4. Si es automático, aplicar política de retención máxima (nunca borrar respaldos Manuales)
            if (tipoNormalizado == "Automatico")
            {
                int retentionCount = Math.Max(1, _config.GetValue<int?>("BackupSettings:RetentionCount") ?? 10);
                var automaticos = await _context.RegistrosRespaldo
                    .Where(r => r.Tipo == "Automatico")
                    .OrderByDescending(r => r.FechaCreacion)
                    .ThenByDescending(r => r.Id)
                    .ToListAsync(ct);

                if (automaticos.Count > retentionCount)
                {
                    var excedentes = automaticos.Skip(retentionCount).ToList();
                    foreach (var viejo in excedentes)
                    {
                        BorrarArchivoZipFisico(viejo);
                        _context.RegistrosRespaldo.Remove(viejo);
                    }
                    await _context.SaveChangesAsync(ct);
                }
            }

            return registro;
        }
        catch (Exception ex)
        {
            if (zipFullPath != null && File.Exists(zipFullPath))
            {
                try { File.Delete(zipFullPath); } catch { /* ignore */ }
            }

            var fallido = new RegistroRespaldo
            {
                NombreArchivo = nombreZip,
                RutaArchivo = string.Empty,
                Tipo = tipoNormalizado,
                Estado = "Error",
                TamanoBytes = 0,
                HashIntegridad = new string('0', 64),
                CreadoPor = actor,
                FechaCreacion = ahora,
                DetalleError = ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message
            };

            try
            {
                _context.RegistrosRespaldo.Add(fallido);
                await _context.SaveChangesAsync(CancellationToken.None);
            }
            catch { /* ignore secondary failure */ }

            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Restaura de manera transaccional la base de datos SQL Server y la carpeta /Storage a partir de un respaldo (.zip).
    /// </summary>
    public async Task<RegistroRespaldo> RestoreBackupAsync(int backupId, string usuario, string? datosMaquina = null, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        string tempExtractDir = Path.Combine(Path.GetTempPath(), $"DocControl_Restore_{Guid.NewGuid():N}");
        string tempStorageBackupDir = Path.Combine(Path.GetTempPath(), $"DocControl_StoragePrev_{Guid.NewGuid():N}");

        try
        {
            var registro = await _context.RegistrosRespaldo.FirstOrDefaultAsync(r => r.Id == backupId, ct);
            if (registro == null)
                throw new KeyNotFoundException($"El respaldo con Id {backupId} no existe.");

            string? zipPath = ResolverRutaZip(registro);
            if (zipPath == null || !File.Exists(zipPath))
                throw new FileNotFoundException($"El archivo físico '{registro.NombreArchivo}' no se encuentra en la carpeta de respaldos.");

            Directory.CreateDirectory(tempExtractDir);
            ZipFile.ExtractToDirectory(zipPath, tempExtractDir, overwriteFiles: true);

            string manifestPath = Path.Combine(tempExtractDir, "manifest.json");
            string snapshotPath = Path.Combine(tempExtractDir, "database_snapshot.json");

            if (!File.Exists(manifestPath) || !File.Exists(snapshotPath))
                throw new InvalidDataException("El archivo .zip no contiene un respaldo válido de DocControl (faltan manifest.json o database_snapshot.json).");

            var manifest = JsonSerializer.Deserialize<BackupManifestDto>(
                await File.ReadAllBytesAsync(manifestPath, ct), JsonOpts)
                ?? throw new InvalidDataException("No se pudo leer manifest.json del respaldo.");

            byte[] snapshotBytes = await File.ReadAllBytesAsync(snapshotPath, ct);
            string actualSha256 = Convert.ToHexString(SHA256.HashData(snapshotBytes)).ToLowerInvariant();

            if (!string.Equals(actualSha256, manifest.SnapshotSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Error de integridad: el hash SHA-256 de database_snapshot.json no coincide con el manifiesto del respaldo.");

            var snapshot = JsonSerializer.Deserialize<DatabaseSnapshotDto>(snapshotBytes, JsonOpts)
                ?? throw new InvalidDataException("No se pudo deserializar database_snapshot.json.");

            string actor = string.IsNullOrWhiteSpace(usuario) ? "Elena Martínez" : usuario.Trim();
            var ahora = DateTime.UtcNow;

            // ── 1. Restauración Transaccional en SQL Server (preservando RegistrosRespaldo) ──
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _context.Database.BeginTransactionAsync(ct);

                // Borrar en orden inverso de llaves foráneas
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM [EnlacesCompartidos];", ct);
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM [SuscripcionesLiberacion];", ct);
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM [Notificaciones];", ct);
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM [Comentarios];", ct);
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM [Bloqueos];", ct);
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM [Versiones];", ct);
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM [Documentos];", ct);
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM [PermisosCarpeta];", ct);
                await _context.Database.ExecuteSqlRawAsync("UPDATE [Carpetas] SET [CarpetaPadreId] = NULL; DELETE FROM [Carpetas];", ct);
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM [Bitacora];", ct);
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM [ConfiguracionesSistema];", ct);

                _context.ChangeTracker.Clear();

                // 1) ConfiguracionesSistema (IDENTITY)
                if (snapshot.ConfiguracionesSistema.Count > 0)
                {
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [ConfiguracionesSistema] ON;", ct);
                    _context.ConfiguracionesSistema.AddRange(snapshot.ConfiguracionesSistema.Select(x => new ConfiguracionSistema
                    {
                        Id = x.Id,
                        Clave = x.Clave,
                        Valor = x.Valor,
                        ActualizadoPor = x.ActualizadoPor,
                        FechaActualizacion = x.FechaActualizacion
                    }));
                    await _context.SaveChangesAsync(ct);
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [ConfiguracionesSistema] OFF;", ct);
                    _context.ChangeTracker.Clear();
                }

                // 2) Carpetas ordenadas topológicamente por jerarquía (padres antes que hijos) (IDENTITY)
                if (snapshot.Carpetas.Count > 0)
                {
                    var carpetasOrdenadas = OrdenarCarpetasTopologicamente(snapshot.Carpetas);
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Carpetas] ON;", ct);
                    foreach (var c in carpetasOrdenadas)
                    {
                        _context.Carpetas.Add(new Carpeta
                        {
                            Id = c.Id,
                            Nombre = c.Nombre,
                            CreadoPor = c.CreadoPor,
                            FechaCreacion = c.FechaCreacion,
                            Color = c.Color,
                            CarpetaPadreId = c.CarpetaPadreId
                        });
                        await _context.SaveChangesAsync(ct);
                    }
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Carpetas] OFF;", ct);
                    _context.ChangeTracker.Clear();
                }

                // 3) PermisosCarpeta (IDENTITY)
                var idsCarpetasValidas = snapshot.Carpetas.Select(c => c.Id).ToHashSet();
                var permisosValidos = snapshot.PermisosCarpeta.Where(p => idsCarpetasValidas.Contains(p.CarpetaId)).ToList();
                if (permisosValidos.Count > 0)
                {
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [PermisosCarpeta] ON;", ct);
                    _context.PermisosCarpeta.AddRange(permisosValidos.Select(p => new PermisoCarpeta
                    {
                        Id = p.Id,
                        CarpetaId = p.CarpetaId,
                        Rol = p.Rol,
                        NivelAcceso = p.NivelAcceso,
                        ActualizadoPor = p.ActualizadoPor,
                        FechaActualizacion = p.FechaActualizacion
                    }));
                    await _context.SaveChangesAsync(ct);
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [PermisosCarpeta] OFF;", ct);
                    _context.ChangeTracker.Clear();
                }

                // 4) Documentos (ValueGeneratedNever: NO usa IDENTITY_INSERT)
                var idsDocumentosValidos = snapshot.Documentos.Select(d => d.Id).ToHashSet();
                if (snapshot.Documentos.Count > 0)
                {
                    _context.Documentos.AddRange(snapshot.Documentos.Select(d => new Documento
                    {
                        Id = d.Id,
                        Codigo = d.Codigo,
                        Nombre = d.Nombre,
                        Tipo = d.Tipo,
                        Departamento = d.Departamento,
                        CarpetaId = d.CarpetaId.HasValue && idsCarpetasValidas.Contains(d.CarpetaId.Value) ? d.CarpetaId : null,
                        VersionActual = d.VersionActual,
                        NumeroVersionActual = d.NumeroVersionActual,
                        Estado = d.Estado,
                        EstadoRevision = d.EstadoRevision,
                        RevisadoPor = d.RevisadoPor,
                        ComentarioRevision = d.ComentarioRevision,
                        FechaCreacion = d.FechaCreacion,
                        FechaUltimaModificacion = d.FechaUltimaModificacion,
                        Eliminado = d.Eliminado,
                        FechaEliminacion = d.FechaEliminacion,
                        EliminadoPor = d.EliminadoPor,
                        Etiquetas = d.Etiquetas ?? string.Empty,
                        TextoIndexado = d.TextoIndexado
                    }));
                    await _context.SaveChangesAsync(ct);
                    _context.ChangeTracker.Clear();
                }

                // 5) Versiones (IDENTITY)
                var versionesValidas = snapshot.VersionesDocumento.Where(v => idsDocumentosValidos.Contains(v.DocumentoId)).ToList();
                if (versionesValidas.Count > 0)
                {
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Versiones] ON;", ct);
                    _context.Versiones.AddRange(versionesValidas.Select(v => new VersionDocumento
                    {
                        Id = v.Id,
                        DocumentoId = v.DocumentoId,
                        NumeroVersion = v.NumeroVersion,
                        VersionTexto = v.VersionTexto,
                        NombreArchivo = v.NombreArchivo,
                        RutaArchivo = v.RutaArchivo,
                        Autor = v.Autor,
                        Comentario = v.Comentario,
                        HashSHA256 = v.HashSHA256,
                        FechaCreacion = v.FechaCreacion,
                        TamanoBytes = v.TamanoBytes,
                        MotivoCambio = v.MotivoCambio ?? string.Empty,
                        TextoIndexado = v.TextoIndexado
                    }));
                    await _context.SaveChangesAsync(ct);
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Versiones] OFF;", ct);
                    _context.ChangeTracker.Clear();
                }

                // 6) Bloqueos (IDENTITY)
                var bloqueosValidos = snapshot.Bloqueos.Where(b => idsDocumentosValidos.Contains(b.DocumentoId)).ToList();
                if (bloqueosValidos.Count > 0)
                {
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Bloqueos] ON;", ct);
                    _context.Bloqueos.AddRange(bloqueosValidos.Select(b => new InfoBloqueo
                    {
                        Id = b.Id,
                        DocumentoId = b.DocumentoId,
                        UsuarioBloqueador = b.UsuarioBloqueador,
                        RolBloqueador = b.RolBloqueador,
                        FechaBloqueo = b.FechaBloqueo,
                        FechaExpiracion = b.FechaExpiracion
                    }));
                    await _context.SaveChangesAsync(ct);
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Bloqueos] OFF;", ct);
                    _context.ChangeTracker.Clear();
                }

                // 7) Comentarios (IDENTITY)
                var comentariosValidos = snapshot.Comentarios.Where(c => idsDocumentosValidos.Contains(c.DocumentoId)).ToList();
                if (comentariosValidos.Count > 0)
                {
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Comentarios] ON;", ct);
                    _context.Comentarios.AddRange(comentariosValidos.Select(c => new ComentarioDocumento
                    {
                        Id = c.Id,
                        DocumentoId = c.DocumentoId,
                        VersionNumero = c.VersionNumero,
                        Autor = c.Autor,
                        RolAutor = c.RolAutor,
                        Texto = c.Texto,
                        FechaCreacion = c.FechaCreacion
                    }));
                    await _context.SaveChangesAsync(ct);
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Comentarios] OFF;", ct);
                    _context.ChangeTracker.Clear();
                }

                // 8) Notificaciones (IDENTITY)
                if (snapshot.Notificaciones.Count > 0)
                {
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Notificaciones] ON;", ct);
                    _context.Notificaciones.AddRange(snapshot.Notificaciones.Select(n => new Notificacion
                    {
                        Id = n.Id,
                        Destinatario = n.Destinatario,
                        RolDestinatario = n.RolDestinatario,
                        Tipo = n.Tipo,
                        Titulo = n.Titulo,
                        Mensaje = n.Mensaje,
                        DocumentoId = n.DocumentoId.HasValue && idsDocumentosValidos.Contains(n.DocumentoId.Value) ? n.DocumentoId : null,
                        CodigoDocumento = n.CodigoDocumento,
                        Leida = n.Leida,
                        FechaCreacion = n.FechaCreacion
                    }));
                    await _context.SaveChangesAsync(ct);
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Notificaciones] OFF;", ct);
                    _context.ChangeTracker.Clear();
                }

                // 9) SuscripcionesLiberacion (IDENTITY)
                var suscripcionesValidas = snapshot.SuscripcionesLiberacion.Where(s => idsDocumentosValidos.Contains(s.DocumentoId)).ToList();
                if (suscripcionesValidas.Count > 0)
                {
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [SuscripcionesLiberacion] ON;", ct);
                    _context.SuscripcionesLiberacion.AddRange(suscripcionesValidas.Select(s => new SuscripcionLiberacion
                    {
                        Id = s.Id,
                        DocumentoId = s.DocumentoId,
                        Usuario = s.Usuario,
                        Rol = s.Rol,
                        FechaSolicitud = s.FechaSolicitud,
                        Notificado = s.Notificado
                    }));
                    await _context.SaveChangesAsync(ct);
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [SuscripcionesLiberacion] OFF;", ct);
                    _context.ChangeTracker.Clear();
                }

                // 10) EnlacesCompartidos (IDENTITY)
                var enlacesValidos = snapshot.EnlacesCompartidos.Where(l => idsDocumentosValidos.Contains(l.DocumentoId)).ToList();
                if (enlacesValidos.Count > 0)
                {
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [EnlacesCompartidos] ON;", ct);
                    _context.EnlacesCompartidos.AddRange(enlacesValidos.Select(l => new EnlaceCompartido
                    {
                        Id = l.Id,
                        Token = l.Token,
                        DocumentoId = l.DocumentoId,
                        VersionId = l.VersionId,
                        CreadoPor = l.CreadoPor,
                        FechaCreacion = l.FechaCreacion,
                        FechaExpiracion = l.FechaExpiracion,
                        Revocado = l.Revocado,
                        FechaRevocacion = l.FechaRevocacion,
                        PermiteDescarga = l.PermiteDescarga,
                        TotalAccesos = l.TotalAccesos
                    }));
                    await _context.SaveChangesAsync(ct);
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [EnlacesCompartidos] OFF;", ct);
                    _context.ChangeTracker.Clear();
                }

                // 11) Bitacora (IDENTITY)
                if (snapshot.Bitacora.Count > 0)
                {
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Bitacora] ON;", ct);
                    _context.Bitacora.AddRange(snapshot.Bitacora.Select(b => new RegistroBitacora
                    {
                        Folio = b.Folio,
                        FechaHora = b.FechaHora,
                        Usuario = b.Usuario,
                        Rol = b.Rol,
                        CodigoDocumento = b.CodigoDocumento,
                        NombreDocumento = b.NombreDocumento,
                        Version = b.Version,
                        Accion = b.Accion,
                        Observaciones = b.Observaciones,
                        DatosMaquina = b.DatosMaquina
                    }));
                    await _context.SaveChangesAsync(ct);
                    await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Bitacora] OFF;", ct);
                    _context.ChangeTracker.Clear();
                }

                // 12) Actualizar FechaUltimaRestauracion del respaldo y registrar RESTAURAR_RESPALDO en Bitacora
                var regEnTx = await _context.RegistrosRespaldo.FirstOrDefaultAsync(r => r.Id == backupId, ct);
                if (regEnTx != null)
                {
                    regEnTx.FechaUltimaRestauracion = ahora;
                }

                _context.Bitacora.Add(new RegistroBitacora
                {
                    FechaHora = ahora,
                    Usuario = actor,
                    Rol = RolUsuario.Administrador,
                    CodigoDocumento = "BACKUP",
                    NombreDocumento = registro.NombreArchivo,
                    Version = "-",
                    Accion = AccionBitacora.RESTAURAR_RESPALDO,
                    Observaciones = $"Sistema restaurado exitosamente desde el respaldo '{registro.NombreArchivo}' (del {registro.FechaCreacion:dd/MM/yyyy HH:mm:ss} UTC). " +
                                    $"Restaurados: {snapshot.Carpetas.Count} carpetas, {snapshot.Documentos.Count} documentos, {snapshot.VersionesDocumento.Count} versiones.",
                    DatosMaquina = datosMaquina ?? $"Servidor: {Environment.MachineName} | Proceso: BackupService"
                });

                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });

            // ── 2. Restauración Física de /Storage con copia preventiva de respaldo ──
            Directory.CreateDirectory(_storagePath);
            Directory.CreateDirectory(tempStorageBackupDir);
            CopiarDirectorio(_storagePath, tempStorageBackupDir);

            try
            {
                LimpiarDirectorio(_storagePath);
                string extractedStorage = Path.Combine(tempExtractDir, "Storage");
                if (Directory.Exists(extractedStorage))
                {
                    CopiarDirectorio(extractedStorage, _storagePath);
                }
            }
            catch
            {
                // Revertir /Storage desde la copia preventiva si fallara el reemplazo en disco
                LimpiarDirectorio(_storagePath);
                CopiarDirectorio(tempStorageBackupDir, _storagePath);
                throw;
            }

            var actualizado = await _context.RegistrosRespaldo.AsNoTracking().FirstAsync(r => r.Id == backupId, ct);
            return actualizado;
        }
        finally
        {
            EliminarDirectorioSeguro(tempExtractDir);
            EliminarDirectorioSeguro(tempStorageBackupDir);
            _lock.Release();
        }
    }

    public string? ResolverRutaZip(RegistroRespaldo registro)
    {
        if (!string.IsNullOrWhiteSpace(registro.RutaArchivo))
        {
            if (File.Exists(registro.RutaArchivo))
                return Path.GetFullPath(registro.RutaArchivo);

            string relRoot = Path.GetFullPath(Path.Combine(_contentRootPath, registro.RutaArchivo));
            if (relRoot.StartsWith(_contentRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(relRoot))
                return relRoot;
        }

        if (!string.IsNullOrWhiteSpace(registro.NombreArchivo))
        {
            string enBackups = Path.GetFullPath(Path.Combine(_backupsPath, Path.GetFileName(registro.NombreArchivo)));
            if (enBackups.StartsWith(_backupsPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(enBackups))
                return enBackups;
        }

        return null;
    }

    public void BorrarArchivoZipFisico(RegistroRespaldo registro)
    {
        try
        {
            string? path = ResolverRutaZip(registro);
            if (path != null && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort
        }
    }

    private static List<CarpetaSnapshotItem> OrdenarCarpetasTopologicamente(List<CarpetaSnapshotItem> carpetas)
    {
        var resultado = new List<CarpetaSnapshotItem>(carpetas.Count);
        var insertadas = new HashSet<int>();
        var pendientes = new List<CarpetaSnapshotItem>(carpetas.OrderBy(c => c.CarpetaPadreId.HasValue).ThenBy(c => c.Id));
        var todosIds = carpetas.Select(c => c.Id).ToHashSet();

        // Si alguna carpeta apunta a un padre que no existe en el snapshot, limpiar su CarpetaPadreId para evitar error de FK
        foreach (var c in pendientes)
        {
            if (c.CarpetaPadreId.HasValue && !todosIds.Contains(c.CarpetaPadreId.Value))
            {
                c.CarpetaPadreId = null;
            }
        }

        int iteraciones = 0;
        int maxIteraciones = pendientes.Count + 2;
        while (pendientes.Count > 0 && iteraciones++ < maxIteraciones)
        {
            var listasParaInsertar = pendientes
                .Where(c => !c.CarpetaPadreId.HasValue || insertadas.Contains(c.CarpetaPadreId.Value))
                .ToList();

            if (listasParaInsertar.Count == 0)
            {
                // Romper cualquier ciclo residual anormal forzando raíz
                var siguiente = pendientes[0];
                siguiente.CarpetaPadreId = null;
                listasParaInsertar.Add(siguiente);
            }

            foreach (var item in listasParaInsertar)
            {
                resultado.Add(item);
                insertadas.Add(item.Id);
                pendientes.Remove(item);
            }
        }

        return resultado;
    }

    private static void CopiarDirectorio(string origen, string destino)
    {
        Directory.CreateDirectory(destino);
        foreach (var dirPath in Directory.GetDirectories(origen, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dirPath.Replace(origen, destino));
        }
        foreach (var filePath in Directory.GetFiles(origen, "*", SearchOption.AllDirectories))
        {
            string destFile = filePath.Replace(origen, destino);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(filePath, destFile, overwrite: true);
        }
    }

    private static void LimpiarDirectorio(string directorio)
    {
        if (!Directory.Exists(directorio)) return;
        foreach (var file in Directory.GetFiles(directorio))
        {
            File.Delete(file);
        }
        foreach (var subDir in Directory.GetDirectories(directorio))
        {
            Directory.Delete(subDir, recursive: true);
        }
    }

    private static void EliminarDirectorioSeguro(string directorio)
    {
        try
        {
            if (Directory.Exists(directorio))
                Directory.Delete(directorio, recursive: true);
        }
        catch { /* ignore */ }
    }
}

// ── DTOs Internos de Snapshot para Serialización Completa sin [JsonIgnore] ──

public class BackupManifestDto
{
    public string SchemaVersion { get; set; } = "1.0";
    public DateTime CreatedUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string Tipo { get; set; } = "Manual";
    public string SnapshotSha256 { get; set; } = string.Empty;
    public int TotalCarpetas { get; set; }
    public int TotalDocumentos { get; set; }
    public int TotalVersiones { get; set; }
    public int TotalArchivosFisicos { get; set; }
    public int TotalRegistrosBitacora { get; set; }
}

public class DatabaseSnapshotDto
{
    public List<ConfiguracionSnapshotItem> ConfiguracionesSistema { get; set; } = new();
    public List<CarpetaSnapshotItem> Carpetas { get; set; } = new();
    public List<PermisoCarpetaSnapshotItem> PermisosCarpeta { get; set; } = new();
    public List<DocumentoSnapshotItem> Documentos { get; set; } = new();
    public List<InfoBloqueoSnapshotItem> Bloqueos { get; set; } = new();
    public List<VersionDocumentoSnapshotItem> VersionesDocumento { get; set; } = new();
    public List<ComentarioSnapshotItem> Comentarios { get; set; } = new();
    public List<NotificacionSnapshotItem> Notificaciones { get; set; } = new();
    public List<SuscripcionSnapshotItem> SuscripcionesLiberacion { get; set; } = new();
    public List<EnlaceCompartidoSnapshotItem> EnlacesCompartidos { get; set; } = new();
    public List<BitacoraSnapshotItem> Bitacora { get; set; } = new();
}

public class ConfiguracionSnapshotItem
{
    public int Id { get; set; }
    public string Clave { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
    public string ActualizadoPor { get; set; } = string.Empty;
    public DateTime FechaActualizacion { get; set; }
}

public class CarpetaSnapshotItem
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string CreadoPor { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
    public string Color { get; set; } = "Azul";
    public int? CarpetaPadreId { get; set; }
}

public class PermisoCarpetaSnapshotItem
{
    public int Id { get; set; }
    public int CarpetaId { get; set; }
    public string Rol { get; set; } = string.Empty;
    public string NivelAcceso { get; set; } = "Lectura";
    public string ActualizadoPor { get; set; } = string.Empty;
    public DateTime FechaActualizacion { get; set; }
}

public class DocumentoSnapshotItem
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public TipoDocumento Tipo { get; set; }
    public string Departamento { get; set; } = string.Empty;
    public int? CarpetaId { get; set; }
    public string VersionActual { get; set; } = "v1.0";
    public int NumeroVersionActual { get; set; } = 1;
    public EstadoDocumento Estado { get; set; }
    public EstadoRevision EstadoRevision { get; set; }
    public string? RevisadoPor { get; set; }
    public string? ComentarioRevision { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaUltimaModificacion { get; set; }
    public bool Eliminado { get; set; }
    public DateTime? FechaEliminacion { get; set; }
    public string? EliminadoPor { get; set; }
    public string Etiquetas { get; set; } = string.Empty;
    public string? TextoIndexado { get; set; }
}

public class InfoBloqueoSnapshotItem
{
    public int Id { get; set; }
    public int DocumentoId { get; set; }
    public string UsuarioBloqueador { get; set; } = string.Empty;
    public RolUsuario RolBloqueador { get; set; }
    public DateTime FechaBloqueo { get; set; }
    public DateTime FechaExpiracion { get; set; }
}

public class VersionDocumentoSnapshotItem
{
    public int Id { get; set; }
    public int DocumentoId { get; set; }
    public int NumeroVersion { get; set; }
    public string VersionTexto { get; set; } = string.Empty;
    public string NombreArchivo { get; set; } = string.Empty;
    public string? RutaArchivo { get; set; }
    public string Autor { get; set; } = string.Empty;
    public string Comentario { get; set; } = string.Empty;
    public string HashSHA256 { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
    public long TamanoBytes { get; set; }
    public string MotivoCambio { get; set; } = string.Empty;
    public string? TextoIndexado { get; set; }
}

public class ComentarioSnapshotItem
{
    public int Id { get; set; }
    public int DocumentoId { get; set; }
    public string VersionNumero { get; set; } = "General";
    public string Autor { get; set; } = string.Empty;
    public string RolAutor { get; set; } = string.Empty;
    public string Texto { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
}

public class NotificacionSnapshotItem
{
    public int Id { get; set; }
    public string Destinatario { get; set; } = string.Empty;
    public string RolDestinatario { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public int? DocumentoId { get; set; }
    public string? CodigoDocumento { get; set; }
    public bool Leida { get; set; }
    public DateTime FechaCreacion { get; set; }
}

public class SuscripcionSnapshotItem
{
    public int Id { get; set; }
    public int DocumentoId { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public DateTime FechaSolicitud { get; set; }
    public bool Notificado { get; set; }
}

public class EnlaceCompartidoSnapshotItem
{
    public int Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public int DocumentoId { get; set; }
    public int? VersionId { get; set; }
    public string CreadoPor { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaExpiracion { get; set; }
    public bool Revocado { get; set; }
    public DateTime? FechaRevocacion { get; set; }
    public bool PermiteDescarga { get; set; }
    public int TotalAccesos { get; set; }
}

public class BitacoraSnapshotItem
{
    public int Folio { get; set; }
    public DateTime FechaHora { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; }
    public string CodigoDocumento { get; set; } = string.Empty;
    public string NombreDocumento { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public AccionBitacora Accion { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public string DatosMaquina { get; set; } = string.Empty;
}
