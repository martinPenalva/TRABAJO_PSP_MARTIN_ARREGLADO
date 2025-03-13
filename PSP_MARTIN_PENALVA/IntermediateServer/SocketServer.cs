using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Text.Json;
using SharedModels;

namespace IntermediateServer
{
    /// <summary>
    /// Servidor socket que maneja conexiones de múltiples clientes
    /// Implementa la comunicación asíncrona mediante sockets
    /// </summary>
    public class SocketServer
    {
        private readonly int _port;
        private Socket _serverSocket;
        private readonly CryptoService _cryptoService;
        private readonly AuditService _auditService;
        private readonly ApiClient _apiClient;
        private readonly ConcurrentDictionary<string, ClientConnection> _connectedClients = new ConcurrentDictionary<string, ClientConnection>();
        private bool _isRunning = false;
        
        // Semáforo para controlar el acceso a recursos compartidos
        private readonly SemaphoreSlim _resourceSemaphore = new SemaphoreSlim(1, 1);

        public event EventHandler<string> ServerStatusChanged;
        public event EventHandler<string> ClientStatusChanged;
        public event EventHandler<string> MessageReceived;

        public SocketServer(int port, AuditService auditService, ApiClient apiClient)
        {
            _port = port;
            _cryptoService = new CryptoService();
            _auditService = auditService;
            _apiClient = apiClient;
        }

        /// <summary>
        /// Inicia el servidor socket de forma asíncrona
        /// </summary>
        public async Task StartAsync()
        {
            if (_isRunning) return;
            
            try
            {
                // Crear socket TCP
                _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                
                // Vincular al puerto
                _serverSocket.Bind(new IPEndPoint(IPAddress.Any, _port));
                
                // Empezar a escuchar con un backlog de 10 conexiones pendientes
                _serverSocket.Listen(10);
                
                _isRunning = true;
                OnServerStatusChanged($"Servidor iniciado en puerto {_port}");
                
                // Aceptar conexiones de forma asíncrona
                await AcceptConnectionsAsync();
            }
            catch (Exception ex)
            {
                OnServerStatusChanged($"Error al iniciar el servidor: {ex.Message}");
                _isRunning = false;
                throw;
            }
        }

        /// <summary>
        /// Acepta conexiones entrantes de forma asíncrona
        /// </summary>
        private async Task AcceptConnectionsAsync()
        {
            while (_isRunning)
            {
                try
                {
                    // Aceptar una nueva conexión
                    Socket clientSocket = await Task.Factory.FromAsync(
                        _serverSocket.BeginAccept, 
                        _serverSocket.EndAccept, 
                        null);
                    
                    // Iniciar el manejo de la conexión en una tarea separada
                    _ = HandleClientAsync(clientSocket);
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        OnServerStatusChanged($"Error al aceptar conexión: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Maneja un cliente conectado de forma asíncrona
        /// </summary>
        private async Task HandleClientAsync(Socket clientSocket)
        {
            string clientId = "unknown";
            string clientIp = ((IPEndPoint)clientSocket.RemoteEndPoint).Address.ToString();
            
            try
            {
                OnClientStatusChanged($"Nueva conexión desde {clientIp}");
                
                // Esperar el mensaje de intercambio de claves para obtener el ID del cliente
                var buffer = new byte[4096];
                int bytesRead = await clientSocket.ReceiveAsync(buffer, SocketFlags.None);
                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                
                var keyExchangeMsg = JsonConvert.DeserializeObject<Message>(message);
                if (keyExchangeMsg?.Type == MessageType.Request && keyExchangeMsg.Action == "KeyExchange")
                {
                    var keyData = JsonConvert.DeserializeObject<dynamic>(keyExchangeMsg.Data);
                    clientId = keyData?.ClientId?.ToString() ?? Guid.NewGuid().ToString();
                    string clientPublicKey = keyData?.PublicKey?.ToString() ?? "";
                    
                    if (!string.IsNullOrEmpty(clientPublicKey))
                    {
                        // Guardar la clave pública del cliente
                        _cryptoService.SetClientPublicKey(clientId, clientPublicKey);
                        
                        // Enviar la clave pública del servidor al cliente
                        var responseMessage = new Message
                        {
                            Type = MessageType.Response,
                            Action = "KeyExchange",
                            Data = JsonConvert.SerializeObject(new
                            {
                                ServerPublicKey = _cryptoService.GetPublicKeyXml()
                            })
                        };
                        
                        string jsonResponse = JsonConvert.SerializeObject(responseMessage);
                        byte[] responseBuffer = Encoding.UTF8.GetBytes(jsonResponse);
                        await clientSocket.SendAsync(responseBuffer, SocketFlags.None);
                        
                        // Registrar el cliente
                        var clientConnection = new ClientConnection
                        {
                            Id = clientId,
                            Socket = clientSocket,
                            IpAddress = clientIp,
                            ConnectedAt = DateTime.Now
                        };
                        
                        _connectedClients[clientId] = clientConnection;
                        OnClientStatusChanged($"Cliente {clientId} registrado desde {clientIp}");
                        
                        // Registrar en el log de auditoría
                        await _auditService.LogOperationAsync(
                            clientId,
                            "Conexión",
                            "Sistema",
                            $"Cliente conectado desde {clientIp}"
                        );
                        
                        // Iniciar recepción de mensajes
                        await ReceiveMessagesAsync(clientConnection);
                    }
                    else
                    {
                        throw new Exception("Clave pública del cliente no recibida");
                    }
                }
                else
                {
                    throw new Exception("Intercambio de claves inválido");
                }
            }
            catch (Exception ex)
            {
                OnClientStatusChanged($"Error con cliente {clientId}: {ex.Message}");
                
                try
                {
                    // Cerrar conexión en caso de error
                    clientSocket.Close();
                    
                    if (_connectedClients.TryRemove(clientId, out _))
                    {
                        OnClientStatusChanged($"Cliente {clientId} desconectado debido a un error");
                    }
                }
                catch
                {
                    // Ignorar errores al cerrar
                }
            }
        }

        /// <summary>
        /// Recibe mensajes de un cliente de forma asíncrona
        /// </summary>
        private async Task ReceiveMessagesAsync(ClientConnection client)
        {
            byte[] buffer = new byte[4096];
            
            while (_isRunning && client.Socket.Connected)
            {
                try
                {
                    // Recibir datos
                    int bytesRead = await client.Socket.ReceiveAsync(buffer, SocketFlags.None);
                    
                    if (bytesRead == 0)
                    {
                        // Conexión cerrada por el cliente
                        break;
                    }
                    
                    // Obtener el mensaje cifrado
                    string encryptedMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    
                    try
                    {
                        // Intentar descifrar
                        byte[] encryptedBytes = Convert.FromBase64String(encryptedMessage);
                        byte[] decryptedBytes = _cryptoService.Decrypt(encryptedBytes);
                        string decryptedJson = Encoding.UTF8.GetString(decryptedBytes);
                        var message = JsonConvert.DeserializeObject<Message>(decryptedJson);
                        
                        if (message != null)
                        {
                            // Verificar la firma digital
                            if (_cryptoService.VerifyClientSignature(client.Id, message.Data, message.Signature))
                            {
                                OnMessageReceived($"Mensaje autenticado de {client.Id}: {message.Action}");
                                
                                // Procesar el mensaje
                                await ProcessMessageAsync(client, message);
                            }
                            else
                            {
                                OnMessageReceived($"Firma inválida en mensaje de {client.Id}");
                                
                                // Registrar intento de falsificación
                                await _auditService.LogOperationAsync(
                                    client.Id,
                                    "Seguridad",
                                    "Sistema",
                                    "Firma digital inválida detectada"
                                );
                            }
                        }
                    }
                    catch
                    {
                        // Si falla el descifrado, podría ser un mensaje no cifrado (como la desconexión)
                        try
                        {
                            var message = JsonConvert.DeserializeObject<Message>(encryptedMessage);
                            if (message != null && message.Action == "Disconnect")
                            {
                                OnMessageReceived($"Cliente {client.Id} solicitó desconexión");
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            OnMessageReceived($"Error al procesar mensaje de {client.Id}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnClientStatusChanged($"Error al recibir de {client.Id}: {ex.Message}");
                    break;
                }
            }
            
            // Cliente desconectado
            if (_connectedClients.TryRemove(client.Id, out _))
            {
                try
                {
                    client.Socket.Close();
                    
                    // Registrar desconexión
                    await _auditService.LogOperationAsync(
                        client.Id,
                        "Desconexión",
                        "Sistema",
                        "Cliente desconectado"
                    );
                    
                    OnClientStatusChanged($"Cliente {client.Id} desconectado");
                }
                catch
                {
                    // Ignorar errores al cerrar
                }
            }
        }

        /// <summary>
        /// Procesa un mensaje recibido de un cliente
        /// </summary>
        private async Task ProcessMessageAsync(ClientConnection client, Message message)
        {
            // Adquirir el semáforo para acceder a recursos compartidos
            await _resourceSemaphore.WaitAsync();
            
            try
            {
                // Registrar la operación en el log de auditoría
                await _auditService.LogOperationAsync(
                    client.Id,
                    message.Action,
                    "API",
                    message.Data
                );
                
                // Procesar según el tipo de mensaje
                switch (message.Action.ToLower())
                {
                    case "create":
                    case "update":
                    case "delete":
                    case "get":
                    case "list":
                        // Reenviar a la API REST
                        var apiResponse = await _apiClient.SendRequestAsync(
                            message.Action.ToLower(),
                            message.Data,
                            client.Id
                        );
                        
                        // Enviar respuesta al cliente
                        await SendResponseToClientAsync(client, message.Action, apiResponse);
                        break;
                        
                    case "disconnect":
                        // Ya se maneja en el método ReceiveMessagesAsync
                        break;
                        
                    default:
                        // Comando desconocido
                        await SendResponseToClientAsync(
                            client,
                            "Error",
                            JsonConvert.SerializeObject(new { Error = "Comando desconocido" })
                        );
                        break;
                }
            }
            finally
            {
                // Liberar el semáforo
                _resourceSemaphore.Release();
            }
        }

        /// <summary>
        /// Envía una respuesta cifrada a un cliente
        /// </summary>
        private async Task SendResponseToClientAsync(ClientConnection client, string action, string data)
        {
            try
            {
                // Crear mensaje de respuesta
                var responseMessage = new Message
                {
                    Type = MessageType.Response,
                    Action = action,
                    Data = data,
                    Signature = _cryptoService.SignMessage(data)
                };
                
                // Convertir a JSON
                string jsonResponse = JsonConvert.SerializeObject(responseMessage);
                
                // Cifrar con la clave pública del cliente
                string encryptedResponse = _cryptoService.EncryptForClient(client.Id, jsonResponse);
                
                // Enviar respuesta cifrada
                byte[] responseBuffer = Encoding.UTF8.GetBytes(encryptedResponse);
                await client.Socket.SendAsync(responseBuffer, SocketFlags.None);
                
                OnMessageReceived($"Respuesta enviada a {client.Id}: {action}");
            }
            catch (Exception ex)
            {
                OnMessageReceived($"Error al enviar respuesta a {client.Id}: {ex.Message}");
            }
        }

        /// <summary>
        /// Detiene el servidor
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning) return;
            
            try
            {
                _isRunning = false;
                
                // Cerrar todas las conexiones de clientes
                foreach (var client in _connectedClients.Values)
                {
                    try
                    {
                        client.Socket.Close();
                    }
                    catch
                    {
                        // Ignorar errores al cerrar
                    }
                }
                
                _connectedClients.Clear();
                
                // Cerrar el socket del servidor
                _serverSocket.Close();
                
                OnServerStatusChanged("Servidor detenido");
            }
            catch (Exception ex)
            {
                OnServerStatusChanged($"Error al detener el servidor: {ex.Message}");
            }
        }

        /// <summary>
        /// Notifica cambios en el estado del servidor
        /// </summary>
        protected virtual void OnServerStatusChanged(string message)
        {
            ServerStatusChanged?.Invoke(this, message);
        }

        /// <summary>
        /// Notifica cambios en el estado de los clientes
        /// </summary>
        protected virtual void OnClientStatusChanged(string message)
        {
            ClientStatusChanged?.Invoke(this, message);
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
    /// Clase que representa una conexión de cliente
    /// </summary>
    public class ClientConnection
    {
        public string Id { get; set; }
        public Socket Socket { get; set; }
        public string IpAddress { get; set; }
        public DateTime ConnectedAt { get; set; }
    }
} 