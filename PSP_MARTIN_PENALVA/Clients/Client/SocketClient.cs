using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Threading;
using System.Collections.Generic;
using SharedModels;

namespace Client
{
    /// <summary>
    /// Cliente socket que se comunica con el servidor intermedio
    /// Implementa la comunicación asíncrona a través de sockets
    /// </summary>
    public class SocketClient
    {
        private readonly string _serverIp;
        private readonly int _serverPort;
        private Socket? _clientSocket;
        private readonly CryptoService _cryptoService;
        private string _clientId;
        private bool _isConnected = false;
        private readonly TaskCompletionSource<bool> _connectedEvent = new TaskCompletionSource<bool>();
        private Thread? _messageThread;

        public event EventHandler<string>? MessageReceived;
        public event EventHandler<string>? ConnectionStatusChanged;

        public SocketClient(string serverIp, int serverPort)
        {
            _serverIp = serverIp;
            _serverPort = serverPort;
            _cryptoService = new CryptoService();
            _clientId = Guid.NewGuid().ToString();
        }

        /// <summary>
        /// Conecta al servidor
        /// </summary>
        public async Task ConnectAsync()
        {
            try
            {
                // Si ya está conectado, no hacer nada
                if (_isConnected)
                {
                    return;
                }
                
                OnConnectionStatusChanged($"Conectando a {_serverIp}:{_serverPort}...");
                
                // Inicializar el socket del cliente
                _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                
                // Conectar al servidor
                await _clientSocket.ConnectAsync(_serverIp, _serverPort);
                
                // Si llega aquí, la conexión fue exitosa
                _isConnected = true;
                OnConnectionStatusChanged($"Conectado al servidor unificado ({_serverIp}:{_serverPort})");
                
                // Intercambiar claves criptográficas
                await ExchangeKeysAsync();
                
                // Iniciar el hilo de escucha de mensajes
                _messageThread = new Thread(ReceiveMessages);
                _messageThread.IsBackground = true;
                _messageThread.Start();
            }
            catch (SocketException se)
            {
                Console.WriteLine("\n====== ERROR DE CONEXIÓN ======");
                Console.WriteLine($"No se pudo conectar al servidor unificado en {_serverIp}:{_serverPort}");
                Console.WriteLine($"Error: {se.Message} (Código: {se.ErrorCode})");
                Console.WriteLine("\nPosibles causas:");
                Console.WriteLine(" - El servidor unificado no está en ejecución");
                Console.WriteLine(" - Compruebe que el servidor se inició con --urls=http://localhost:8080");
                Console.WriteLine(" - El puerto 8080 está bloqueado por un firewall");
                Console.WriteLine(" - Otro proceso está utilizando el puerto 8080");
                Console.WriteLine(" - Puede haber un problema de configuración en el servidor\n");
                Console.WriteLine("Sugerencia: Verifique que el servidor esté funcionando correctamente\n");
                
                OnConnectionStatusChanged($"Error de conexión: {se.Message}");
                _isConnected = false;
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine("\n====== ERROR DE CONEXIÓN ======");
                Console.WriteLine($"Error al conectar al servidor unificado en {_serverIp}:{_serverPort}");
                Console.WriteLine($"Tipo de error: {ex.GetType().Name}");
                Console.WriteLine($"Mensaje: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Error interno: {ex.InnerException.Message}");
                }
                Console.WriteLine("\nRevise la configuración y asegúrese de que el servidor unificado esté en funcionamiento.");
                Console.WriteLine("Sugerencia: Verifique que el servidor esté funcionando correctamente\n");
                
                OnConnectionStatusChanged($"Error al conectar: {ex.Message}");
                _isConnected = false;
                throw;
            }
        }

        /// <summary>
        /// Método que se ejecuta en un hilo separado para recibir mensajes
        /// </summary>
        private void ReceiveMessages()
        {
            byte[] buffer = new byte[4096];
            
            while (_isConnected && _clientSocket != null)
            {
                try
                {
                    // Recibir datos
                    int bytesRead = _clientSocket.Receive(buffer);
                    
                    if (bytesRead == 0)
                    {
                        // Conexión cerrada por el servidor
                        _isConnected = false;
                        OnConnectionStatusChanged("Conexión cerrada por el servidor");
                        break;
                    }
                    
                    // Procesar el mensaje recibido
                    string encryptedMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    
                    try
                    {
                        // Intentar descifrar el mensaje
                        byte[] encryptedBytes = Convert.FromBase64String(encryptedMessage);
                        byte[] decryptedBytes = _cryptoService.Decrypt(encryptedBytes);
                        string decryptedJson = Encoding.UTF8.GetString(decryptedBytes);
                        var message = JsonConvert.DeserializeObject<Message>(decryptedJson);
                        
                        if (message != null)
                        {
                            // Notificar el mensaje recibido
                            OnMessageReceived($"Mensaje recibido: {message.Action}");
                            
                            // Procesar el mensaje
                            ProcessMessage(message);
                        }
                    }
                    catch
                    {
                        // Si falla el descifrado, podría ser un mensaje no cifrado
                        try
                        {
                            var message = JsonConvert.DeserializeObject<Message>(encryptedMessage);
                            if (message != null)
                            {
                                OnMessageReceived($"Mensaje sin cifrar recibido: {message.Action}");
                                ProcessMessage(message);
                            }
                        }
                        catch (Exception ex)
                        {
                            OnMessageReceived($"Error al procesar mensaje: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (_isConnected)
                    {
                        OnConnectionStatusChanged($"Error al recibir: {ex.Message}");
                        
                        // Intentar reconectar o manejar la desconexión
                        _isConnected = false;
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Intercambia claves criptográficas con el servidor
        /// </summary>
        private async Task ExchangeKeysAsync()
        {
            try
            {
                // Enviar ID del cliente y clave pública
                var keyExchangeMessage = new Message
                {
                    Type = MessageType.Request,
                    Action = "KeyExchange",
                    Data = JsonConvert.SerializeObject(new
                    {
                        ClientId = _clientId,
                        PublicKey = _cryptoService.GetPublicKeyXml()
                    })
                };
                
                await SendMessageAsync(keyExchangeMessage, false);
                
                // Recibir la clave pública del servidor
                var responseBuffer = new byte[4096];
                int bytesRead = await _clientSocket!.ReceiveAsync(responseBuffer, SocketFlags.None);
                string responseJson = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);
                
                var response = JsonConvert.DeserializeObject<Message>(responseJson);
                if (response?.Type == MessageType.Response && response.Action == "KeyExchange")
                {
                    var keyData = JsonConvert.DeserializeObject<dynamic>(response.Data);
                    string serverPublicKey = keyData?.ServerPublicKey?.ToString() ?? "";
                    
                    if (!string.IsNullOrEmpty(serverPublicKey))
                    {
                        _cryptoService.SetServerPublicKey(serverPublicKey);
                        OnConnectionStatusChanged("Intercambio de claves completado");
                    }
                    else
                    {
                        throw new Exception("No se recibió la clave pública del servidor");
                    }
                }
                else
                {
                    throw new Exception("Respuesta inválida durante el intercambio de claves");
                }
            }
            catch (Exception ex)
            {
                OnConnectionStatusChanged($"Error en el intercambio de claves: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Envía un mensaje al servidor de forma asíncrona, opcionalmente lo cifra
        /// </summary>
        public async Task SendMessageAsync(Message message, bool encrypt = true)
        {
            if (!_isConnected || _clientSocket == null)
            {
                throw new InvalidOperationException("No hay conexión con el servidor");
            }
            
            try
            {
                // Añadir firma digital
                string messageData = message.Data;
                message.Signature = _cryptoService.SignMessage(messageData);
                
                // Convertir a JSON
                string jsonMessage = JsonConvert.SerializeObject(message);
                
                // Cifrar si es necesario y si tenemos la clave del servidor
                if (encrypt)
                {
                    jsonMessage = _cryptoService.EncryptForServer(jsonMessage);
                }
                
                // Enviar
                byte[] buffer = Encoding.UTF8.GetBytes(jsonMessage);
                await _clientSocket.SendAsync(buffer, SocketFlags.None);
            }
            catch (Exception ex)
            {
                OnConnectionStatusChanged($"Error al enviar mensaje: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Procesa un mensaje recibido del servidor
        /// </summary>
        private void ProcessMessage(Message message)
        {
            try
            {
                OnMessageReceived($"Procesando mensaje: {message.Action}");
                
                // Procesar según el tipo de mensaje
                if (message.Type == MessageType.Response)
                {
                    switch (message.Action.ToLower())
                    {
                        case "list":
                            ProcessListResponse(message.Data);
                            break;
                            
                        case "get":
                            ProcessGetResponse(message.Data);
                            break;
                            
                        case "create":
                            ProcessCreateResponse(message.Data);
                            break;
                            
                        case "update":
                            ProcessUpdateResponse(message.Data);
                            break;
                            
                        case "delete":
                            ProcessDeleteResponse(message.Data);
                            break;
                            
                        case "error":
                            ProcessErrorResponse(message.Data);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                OnMessageReceived($"Error al procesar mensaje: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Procesa la respuesta a una petición de listar reservas
        /// </summary>
        private void ProcessListResponse(string data)
        {
            try
            {
                // Verificar si hay un error en la respuesta
                if (data.Contains("Error"))
                {
                    var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(data);
                    Console.WriteLine($"\nError al listar reservas: {errorResponse?.Message}");
                    return;
                }
                
                // Intentar deserializar como lista de reservas
                var reservations = JsonConvert.DeserializeObject<List<Reservation>>(data);
                
                Console.WriteLine("\n=== LISTADO DE RESERVAS ===");
                
                if (reservations == null || reservations.Count == 0)
                {
                    Console.WriteLine("No hay reservas disponibles.");
                    return;
                }
                
                foreach (var reservation in reservations)
                {
                    Console.WriteLine($"ID: {reservation.Id}");
                    Console.WriteLine($"Cliente: {reservation.CustomerName}");
                    Console.WriteLine($"Fecha: {reservation.ReservationDateTime}");
                    Console.WriteLine($"Comensales: {reservation.NumberOfGuests}");
                    Console.WriteLine($"Mesa: {reservation.TableNumber}");
                    Console.WriteLine($"Estado: {(reservation.IsConfirmed ? "Confirmada" : "Pendiente")}");
                    Console.WriteLine("------------------------------");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError al procesar la lista de reservas: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Procesa la respuesta a una petición de detalle de reserva
        /// </summary>
        private void ProcessGetResponse(string data)
        {
            try
            {
                // Verificar si hay un error en la respuesta
                if (data.Contains("Error"))
                {
                    var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(data);
                    Console.WriteLine($"\nError al obtener reserva: {errorResponse?.Message}");
                    return;
                }
                
                // Intentar deserializar como una reserva
                var reservation = JsonConvert.DeserializeObject<Reservation>(data);
                
                if (reservation == null)
                {
                    Console.WriteLine("\nNo se pudo obtener la información de la reserva.");
                    return;
                }
                
                Console.WriteLine("\n=== DETALLES DE RESERVA ===");
                Console.WriteLine($"ID: {reservation.Id}");
                Console.WriteLine($"Cliente: {reservation.CustomerName}");
                Console.WriteLine($"Teléfono: {reservation.PhoneNumber}");
                Console.WriteLine($"Fecha y hora: {reservation.ReservationDateTime}");
                Console.WriteLine($"Comensales: {reservation.NumberOfGuests}");
                Console.WriteLine($"Mesa: {reservation.TableNumber}");
                Console.WriteLine($"Peticiones especiales: {reservation.SpecialRequests ?? "Ninguna"}");
                Console.WriteLine($"Estado: {(reservation.IsConfirmed ? "Confirmada" : "Pendiente")}");
                Console.WriteLine($"Creada: {reservation.CreatedAt}");
                if (reservation.LastModifiedAt.HasValue)
                {
                    Console.WriteLine($"Última modificación: {reservation.LastModifiedAt}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError al procesar los detalles de la reserva: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Procesa la respuesta a una petición de crear reserva
        /// </summary>
        private void ProcessCreateResponse(string data)
        {
            try
            {
                // Verificar si hay un error en la respuesta
                if (data.Contains("Error"))
                {
                    var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(data);
                    Console.WriteLine($"\nError al crear reserva: {errorResponse?.Message}");
                    return;
                }
                
                // Intentar deserializar como una reserva
                var reservation = JsonConvert.DeserializeObject<Reservation>(data);
                
                if (reservation == null)
                {
                    Console.WriteLine("\nNo se pudo procesar la respuesta de creación de reserva.");
                    return;
                }
                
                Console.WriteLine("\n=== RESERVA CREADA EXITOSAMENTE ===");
                Console.WriteLine($"ID de la reserva: {reservation.Id}");
                Console.WriteLine($"Cliente: {reservation.CustomerName}");
                Console.WriteLine($"Fecha y hora: {reservation.ReservationDateTime}");
                Console.WriteLine($"Mesa asignada: {reservation.TableNumber}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError al procesar la respuesta de creación: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Procesa la respuesta a una petición de actualizar reserva
        /// </summary>
        private void ProcessUpdateResponse(string data)
        {
            try
            {
                // Verificar si hay un error en la respuesta
                if (data.Contains("Error"))
                {
                    var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(data);
                    Console.WriteLine($"\nError al modificar reserva: {errorResponse?.Message}");
                    return;
                }
                
                // Intentar deserializar como una reserva
                var reservation = JsonConvert.DeserializeObject<Reservation>(data);
                
                if (reservation == null)
                {
                    Console.WriteLine("\nNo se pudo procesar la respuesta de modificación de reserva.");
                    return;
                }
                
                Console.WriteLine("\n=== RESERVA MODIFICADA EXITOSAMENTE ===");
                Console.WriteLine($"ID de la reserva: {reservation.Id}");
                Console.WriteLine($"Cliente: {reservation.CustomerName}");
                Console.WriteLine($"Fecha y hora: {reservation.ReservationDateTime}");
                Console.WriteLine($"Mesa asignada: {reservation.TableNumber}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError al procesar la respuesta de modificación: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Procesa la respuesta a una petición de eliminar reserva
        /// </summary>
        private void ProcessDeleteResponse(string data)
        {
            try
            {
                // Verificar si hay un error en la respuesta
                if (data.Contains("Error"))
                {
                    var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(data);
                    Console.WriteLine($"\nError al eliminar reserva: {errorResponse?.Message}");
                    return;
                }
                
                Console.WriteLine("\n=== RESERVA ELIMINADA EXITOSAMENTE ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError al procesar la respuesta de eliminación: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Procesa un mensaje de error
        /// </summary>
        private void ProcessErrorResponse(string data)
        {
            try
            {
                var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(data);
                Console.WriteLine($"\nError: {errorResponse?.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError al procesar mensaje de error: {ex.Message}");
            }
        }

        /// <summary>
        /// Cierra la conexión con el servidor
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_isConnected && _clientSocket != null)
            {
                try
                {
                    // Enviar mensaje de desconexión
                    var disconnectMessage = new Message
                    {
                        Type = MessageType.Request,
                        Action = "Disconnect",
                        Data = JsonConvert.SerializeObject(new { ClientId = _clientId })
                    };
                    
                    await SendMessageAsync(disconnectMessage);
                    
                    // Cerrar socket
                    _clientSocket.Shutdown(SocketShutdown.Both);
                    _clientSocket.Close();
                    _isConnected = false;
                    
                    OnConnectionStatusChanged("Desconectado del servidor");
                }
                catch (Exception ex)
                {
                    OnConnectionStatusChanged($"Error al desconectar: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Verifica si el cliente está conectado al servidor
        /// </summary>
        public bool IsConnected()
        {
            return _isConnected;
        }

        /// <summary>
        /// Obtiene el ID único del cliente
        /// </summary>
        public string GetClientId()
        {
            return _clientId;
        }

        /// <summary>
        /// Notifica cambios en el estado de la conexión
        /// </summary>
        protected virtual void OnConnectionStatusChanged(string message)
        {
            ConnectionStatusChanged?.Invoke(this, message);
        }

        /// <summary>
        /// Notifica mensajes recibidos
        /// </summary>
        protected virtual void OnMessageReceived(string message)
        {
            MessageReceived?.Invoke(this, message);
        }
    }
    
    /// <summary>
    /// Clase auxiliar para deserializar respuestas de error
    /// </summary>
    public class ErrorResponse
    {
        public bool Error { get; set; }
        public string Message { get; set; } = string.Empty;
    }
} 