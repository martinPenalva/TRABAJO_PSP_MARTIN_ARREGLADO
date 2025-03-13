using System;
using System.Threading.Tasks;
using IntermediateServer;
using SharedModels;

namespace IntermediateServer
{
    /// <summary>
    /// Programa principal del servidor intermedio
    /// Configura y ejecuta el servidor socket que maneja las conexiones de los clientes
    /// y se comunica con la API REST
    /// </summary>
    class Program
    {
        private static SocketServer _socketServer;
        private static AuditService _auditService;
        private static ApiClient _apiClient;
        private static bool _isRunning = true;

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== SERVIDOR UNIFICADO (API + SOCKET) ===");
            Console.WriteLine("Este servidor implementa la comunicación cliente-servidor y API en un único puerto");
            Console.WriteLine("Cifrado asimétrico y registro de auditoría incluidos");
            Console.WriteLine();

            try
            {
                // Configurar puerto único para el servidor
                int serverPort = 8080;
                Console.WriteLine($"Configurando servidor unificado en puerto {serverPort}...");
                
                // Configurar URL de la API REST (en el puerto 5138 donde realmente se ejecuta)
                string apiUrl = "http://localhost:5138";
                Console.WriteLine($"Configurando cliente API para {apiUrl}...");
                
                // Inicializar servicios
                _auditService = new AuditService();
                _apiClient = new ApiClient(apiUrl, _auditService);
                _socketServer = new SocketServer(serverPort, _auditService, _apiClient);
                
                // Suscribirse a eventos del servidor
                _socketServer.ServerStatusChanged += (sender, message) => Console.WriteLine($"[SERVER] {message}");
                _socketServer.ClientStatusChanged += (sender, message) => Console.WriteLine($"[CLIENT] {message}");
                _socketServer.MessageReceived += (sender, message) => Console.WriteLine($"[MESSAGE] {message}");
                
                // Iniciar el servidor
                Console.WriteLine("Iniciando servidor unificado...");
                await _socketServer.StartAsync();
                
                // Mostrar mensaje claro de que está escuchando
                Console.WriteLine($"\n[SERVER] El servidor está escuchando activamente en el puerto {serverPort}");
                Console.WriteLine($"[API] API REST disponible en {apiUrl}/reservations");
                
                // Mantener el programa en ejecución hasta que se presione Ctrl+C
                Console.WriteLine("\nServidor unificado en ejecución. Presione Ctrl+C para detener.\n");
                
                // Manejar la señal de cierre
                Console.CancelKeyPress += async (sender, e) =>
                {
                    e.Cancel = true; // Evita que el programa se cierre inmediatamente
                    await ShutdownAsync();
                };
                
                // Mantener el programa en ejecución
                while (_isRunning)
                {
                    await Task.Delay(1000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fatal: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// Realiza un apagado controlado del servidor
        /// </summary>
        private static async Task ShutdownAsync()
        {
            Console.WriteLine("\nApagando el servidor...");
            
            try
            {
                if (_socketServer != null)
                {
                    await _socketServer.StopAsync();
                }
                
                Console.WriteLine("Servidor detenido correctamente.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error durante el apagado: {ex.Message}");
            }
            finally
            {
                _isRunning = false;
            }
        }
    }
}
