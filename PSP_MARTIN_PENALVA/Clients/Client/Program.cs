using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Client;
using SharedModels;

namespace Client
{
    /// <summary>
    /// Programa principal del cliente
    /// Implementa una interfaz de consola para interactuar con el servidor intermedio
    /// </summary>
    class Program
    {
        private static SocketClient? _socketClient;
        private static bool _isRunning = true;

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== CLIENTE DE RESERVAS ===");
            Console.WriteLine("Este cliente se comunica con el servidor usando sockets");
            Console.WriteLine("Implementa cifrado asimétrico para la seguridad de las comunicaciones");
            Console.WriteLine();

            try
            {
                // Configurar la conexión al servidor
                string serverIp = "localhost";
                int serverPort = 8080;
                
                Console.WriteLine($"Conectando al servidor unificado {serverIp}:{serverPort}...");
                
                // Inicializar el cliente socket
                _socketClient = new SocketClient(serverIp, serverPort);
                
                // Suscribirse a eventos
                _socketClient.ConnectionStatusChanged += (sender, message) => Console.WriteLine($"[CONEXIÓN] {message}");
                _socketClient.MessageReceived += (sender, message) => Console.WriteLine($"[MENSAJE] {message}");
                
                // Conectar al servidor
                await _socketClient.ConnectAsync();
                
                // Si la conexión es exitosa, mostrar el menú principal
                if (_socketClient.IsConnected())
                {
                    Console.WriteLine($"\nConectado al servidor unificado con ID de cliente: {_socketClient.GetClientId()}");
                    
                    // Mostrar menú y procesar opciones
                    await ShowMenuAsync();
                }
                else
                {
                    Console.WriteLine("No se pudo conectar al servidor unificado. El programa se cerrará.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("Detalles: " + ex.ToString());
            }
            
            Console.WriteLine("Programa finalizado.");
        }

        /// <summary>
        /// Muestra el menú principal y procesa las opciones del usuario
        /// </summary>
        private static async Task ShowMenuAsync()
        {
            while (_isRunning && _socketClient != null && _socketClient.IsConnected())
            {
                Console.WriteLine("\n=== MENÚ PRINCIPAL ===");
                Console.WriteLine("1. Listar reservas");
                Console.WriteLine("2. Ver detalles de una reserva");
                Console.WriteLine("3. Crear nueva reserva");
                Console.WriteLine("4. Modificar reserva");
                Console.WriteLine("5. Eliminar reserva");
                Console.WriteLine("0. Salir");
                Console.Write("Seleccione una opción: ");
                
                string? option = Console.ReadLine();
                
                switch (option)
                {
                    case "1":
                        await ListReservationsAsync();
                        break;
                    case "2":
                        await GetReservationDetailsAsync();
                        break;
                    case "3":
                        await CreateReservationAsync();
                        break;
                    case "4":
                        await UpdateReservationAsync();
                        break;
                    case "5":
                        await DeleteReservationAsync();
                        break;
                    case "0":
                        await DisconnectAsync();
                        _isRunning = false;
                        break;
                    default:
                        Console.WriteLine("Opción no válida. Intente de nuevo.");
                        break;
                }
            }
        }

        /// <summary>
        /// Lista todas las reservas disponibles
        /// </summary>
        private static async Task ListReservationsAsync()
        {
            try
            {
                Console.WriteLine("\n=== LISTAR RESERVAS ===");
                
                // Crear mensaje para el servidor
                var message = Message.Create(
                    MessageType.Request,
                    "list",
                    new { } // Sin datos adicionales para listar
                );
                
                // Enviar mensaje
                if (_socketClient != null)
                {
                    await _socketClient.SendMessageAsync(message);
                    Console.WriteLine("Solicitud enviada. La respuesta llegará a través del evento MessageReceived.");
                }
                else
                {
                    Console.WriteLine("Error: No hay conexión con el servidor.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al listar reservas: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtiene los detalles de una reserva específica
        /// </summary>
        private static async Task GetReservationDetailsAsync()
        {
            try
            {
                Console.WriteLine("\n=== VER DETALLES DE RESERVA ===");
                Console.Write("Introduzca el ID de la reserva: ");
                
                if (!long.TryParse(Console.ReadLine(), out long reservationId))
                {
                    Console.WriteLine("ID no válido. Debe ser un número.");
                    return;
                }
                
                // Crear mensaje para el servidor
                var message = Message.Create(
                    MessageType.Request,
                    "get",
                    new { Id = reservationId }
                );
                
                // Enviar mensaje
                if (_socketClient != null)
                {
                    await _socketClient.SendMessageAsync(message);
                    Console.WriteLine("Solicitud enviada. La respuesta llegará a través del evento MessageReceived.");
                }
                else
                {
                    Console.WriteLine("Error: No hay conexión con el servidor.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al obtener detalles de reserva: {ex.Message}");
            }
        }

        /// <summary>
        /// Crea una nueva reserva
        /// </summary>
        private static async Task CreateReservationAsync()
        {
            try
            {
                Console.WriteLine("\n=== CREAR NUEVA RESERVA ===");
                
                // Recopilar datos de la reserva
                var reservation = new Reservation();
                
                Console.Write("Nombre del cliente: ");
                reservation.CustomerName = Console.ReadLine() ?? "";
                
                Console.Write("Número de teléfono: ");
                reservation.PhoneNumber = Console.ReadLine() ?? "";
                
                Console.Write("Fecha y hora (yyyy-MM-dd HH:mm): ");
                if (DateTime.TryParse(Console.ReadLine(), out DateTime reservationTime))
                {
                    reservation.ReservationDateTime = reservationTime;
                }
                else
                {
                    Console.WriteLine("Formato de fecha no válido.");
                    return;
                }
                
                Console.Write("Número de comensales: ");
                if (int.TryParse(Console.ReadLine(), out int guests))
                {
                    reservation.NumberOfGuests = guests;
                }
                else
                {
                    Console.WriteLine("Número no válido.");
                    return;
                }
                
                Console.Write("Número de mesa (opcional): ");
                string? tableInput = Console.ReadLine();
                if (!string.IsNullOrEmpty(tableInput) && int.TryParse(tableInput, out int tableNumber))
                {
                    reservation.TableNumber = tableNumber;
                }
                
                Console.Write("Peticiones especiales (opcional): ");
                reservation.SpecialRequests = Console.ReadLine();
                
                // Establecer valores predeterminados
                reservation.CreatedAt = DateTime.Now;
                reservation.IsConfirmed = false;
                
                // Crear mensaje para el servidor
                var message = Message.Create(
                    MessageType.Request,
                    "create",
                    reservation
                );
                
                // Enviar mensaje
                if (_socketClient != null)
                {
                    await _socketClient.SendMessageAsync(message);
                    Console.WriteLine("Solicitud enviada. La respuesta llegará a través del evento MessageReceived.");
                }
                else
                {
                    Console.WriteLine("Error: No hay conexión con el servidor.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al crear reserva: {ex.Message}");
            }
        }

        /// <summary>
        /// Actualiza una reserva existente
        /// </summary>
        private static async Task UpdateReservationAsync()
        {
            try
            {
                Console.WriteLine("\n=== MODIFICAR RESERVA ===");
                Console.Write("Introduzca el ID de la reserva a modificar: ");
                
                if (!long.TryParse(Console.ReadLine(), out long reservationId))
                {
                    Console.WriteLine("ID no válido. Debe ser un número.");
                    return;
                }
                
                // En un caso real, primero obtendríamos la reserva existente
                // y luego la modificaríamos. Para simplificar, creamos una nueva.
                var reservation = new Reservation { Id = reservationId };
                
                Console.Write("Nombre del cliente: ");
                reservation.CustomerName = Console.ReadLine() ?? "";
                
                Console.Write("Número de teléfono: ");
                reservation.PhoneNumber = Console.ReadLine() ?? "";
                
                Console.Write("Fecha y hora (yyyy-MM-dd HH:mm): ");
                if (DateTime.TryParse(Console.ReadLine(), out DateTime reservationTime))
                {
                    reservation.ReservationDateTime = reservationTime;
                }
                else
                {
                    Console.WriteLine("Formato de fecha no válido.");
                    return;
                }
                
                Console.Write("Número de comensales: ");
                if (int.TryParse(Console.ReadLine(), out int guests))
                {
                    reservation.NumberOfGuests = guests;
                }
                else
                {
                    Console.WriteLine("Número no válido.");
                    return;
                }
                
                Console.Write("Número de mesa: ");
                if (int.TryParse(Console.ReadLine(), out int tableNumber))
                {
                    reservation.TableNumber = tableNumber;
                }
                
                Console.Write("Peticiones especiales (opcional): ");
                reservation.SpecialRequests = Console.ReadLine();
                
                Console.Write("¿Confirmada? (s/n): ");
                reservation.IsConfirmed = Console.ReadLine()?.ToLower() == "s";
                
                // Actualizar fecha de modificación
                reservation.LastModifiedAt = DateTime.Now;
                
                // Crear mensaje para el servidor
                var message = Message.Create(
                    MessageType.Request,
                    "update",
                    reservation
                );
                
                // Enviar mensaje
                if (_socketClient != null)
                {
                    await _socketClient.SendMessageAsync(message);
                    Console.WriteLine("Solicitud enviada. La respuesta llegará a través del evento MessageReceived.");
                }
                else
                {
                    Console.WriteLine("Error: No hay conexión con el servidor.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al modificar reserva: {ex.Message}");
            }
        }

        /// <summary>
        /// Elimina una reserva existente
        /// </summary>
        private static async Task DeleteReservationAsync()
        {
            try
            {
                Console.WriteLine("\n=== ELIMINAR RESERVA ===");
                Console.Write("Introduzca el ID de la reserva a eliminar: ");
                
                if (!long.TryParse(Console.ReadLine(), out long reservationId))
                {
                    Console.WriteLine("ID no válido. Debe ser un número.");
                    return;
                }
                
                // Crear mensaje para el servidor
                var message = Message.Create(
                    MessageType.Request,
                    "delete",
                    new { Id = reservationId }
                );
                
                // Enviar mensaje
                if (_socketClient != null)
                {
                    await _socketClient.SendMessageAsync(message);
                    Console.WriteLine("Solicitud enviada. La respuesta llegará a través del evento MessageReceived.");
                }
                else
                {
                    Console.WriteLine("Error: No hay conexión con el servidor.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al eliminar reserva: {ex.Message}");
            }
        }

        /// <summary>
        /// Desconecta del servidor
        /// </summary>
        private static async Task DisconnectAsync()
        {
            try
            {
                Console.WriteLine("\nDesconectando del servidor...");
                if (_socketClient != null)
                {
                    await _socketClient.DisconnectAsync();
                    Console.WriteLine("Desconectado correctamente.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al desconectar: {ex.Message}");
            }
        }
    }
}
