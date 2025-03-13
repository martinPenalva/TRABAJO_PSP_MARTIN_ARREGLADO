using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SharedModels;

namespace IntermediateServer
{
    /// <summary>
    /// Servicio para el registro unidireccional de operaciones (audit logging)
    /// Cumple con el requisito RA5 de registrar quién realiza la petición del CRUD
    /// </summary>
    public class AuditService
    {
        private readonly string _logFilePath;
        private static readonly object _lockObject = new object();
        private List<AuditLog> _auditLogs = new List<AuditLog>();
        private long _nextId = 1;

        public AuditService(string logDirectory = "logs")
        {
            // Asegurar que el directorio de logs existe
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            // Configurar la ruta del archivo de registro
            _logFilePath = Path.Combine(logDirectory, "audit_log.json");
            
            // Cargar logs existentes si el archivo existe
            if (File.Exists(_logFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_logFilePath);
                    _auditLogs = JsonSerializer.Deserialize<List<AuditLog>>(json) ?? new List<AuditLog>();
                    
                    // Determinar el próximo ID
                    if (_auditLogs.Count > 0)
                    {
                        _nextId = _auditLogs.Max(log => log.Id) + 1;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al cargar los logs: {ex.Message}");
                    _auditLogs = new List<AuditLog>();
                }
            }
            
            Console.WriteLine($"Servicio de auditoría inicializado. Ruta de logs: {_logFilePath}");
        }

        /// <summary>
        /// Registra una operación en el log de auditoría
        /// </summary>
        public async Task LogOperationAsync(string clientId, string operation, string resource, string details)
        {
            var logEntry = new AuditLog
            {
                Id = _nextId++,
                ClientId = clientId,
                Timestamp = DateTime.Now,
                Operation = operation,
                Resource = resource,
                Details = details
            };
            
            lock (_lockObject)
            {
                _auditLogs.Add(logEntry);
            }
            
            // Guardar en disco de forma asíncrona
            await SaveLogsAsync();
            
            Console.WriteLine($"[AUDIT] Cliente: {clientId}, Operación: {operation}, Recurso: {resource}");
        }
        
        /// <summary>
        /// Guarda los logs en disco
        /// </summary>
        private async Task SaveLogsAsync()
        {
            try
            {
                List<AuditLog> logsCopy;
                
                lock (_lockObject)
                {
                    logsCopy = new List<AuditLog>(_auditLogs);
                }
                
                string json = JsonSerializer.Serialize(logsCopy, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_logFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al guardar los logs: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Obtiene todos los registros de auditoría
        /// </summary>
        public List<AuditLog> GetAllLogs()
        {
            lock (_lockObject)
            {
                return new List<AuditLog>(_auditLogs);
            }
        }
        
        /// <summary>
        /// Obtiene los logs filtrados por cliente
        /// </summary>
        public List<AuditLog> GetLogsByClient(string clientId)
        {
            lock (_lockObject)
            {
                return _auditLogs.Where(log => log.ClientId == clientId).ToList();
            }
        }
        
        /// <summary>
        /// Obtiene los logs filtrados por operación
        /// </summary>
        public List<AuditLog> GetLogsByOperation(string operation)
        {
            lock (_lockObject)
            {
                return _auditLogs.Where(log => log.Operation == operation).ToList();
            }
        }
    }
} 