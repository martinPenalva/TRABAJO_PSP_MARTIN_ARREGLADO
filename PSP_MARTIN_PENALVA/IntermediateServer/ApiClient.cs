using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SharedModels;

namespace IntermediateServer
{
    /// <summary>
    /// Cliente para comunicarse con la API REST
    /// Implementa la comunicación entre el servidor intermedio y la API
    /// </summary>
    public class ApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly AuditService _auditService;

        public ApiClient(string baseUrl, AuditService auditService)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _auditService = auditService;
            
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            
            Console.WriteLine($"Cliente API inicializado para {_baseUrl}");
        }

        /// <summary>
        /// Envía una solicitud a la API REST y registra la operación en el log de auditoría
        /// </summary>
        public async Task<string> SendRequestAsync(string action, string data, string clientId)
        {
            try
            {
                // Determinar el endpoint y método HTTP basado en la acción
                HttpMethod method = DetermineHttpMethod(action);
                
                // Construir la URL del endpoint con el ID si es necesario
                string endpoint = "api/reservations";
                
                // Para acciones que requieren un ID, extraerlo del JSON y añadirlo a la URL
                if ((action == "get" || action == "update" || action == "delete") && !string.IsNullOrEmpty(data))
                {
                    try
                    {
                        // Intentar extraer el ID desde los datos JSON
                        var jsonData = JsonConvert.DeserializeObject<dynamic>(data);
                        if (jsonData != null && jsonData.Id != null)
                        {
                            endpoint = $"{endpoint}/{jsonData.Id}";
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error al extraer ID de los datos: {ex.Message}");
                    }
                }
                
                // Crear la solicitud HTTP
                var request = new HttpRequestMessage(method, $"{_baseUrl}/{endpoint}");
                
                // Añadir encabezado para identificar al cliente que realiza la solicitud
                request.Headers.Add("X-Client-Id", clientId);
                
                // Añadir cuerpo si es necesario
                if (method != HttpMethod.Get && !string.IsNullOrEmpty(data))
                {
                    request.Content = new StringContent(data, Encoding.UTF8, "application/json");
                }
                
                // Registrar la operación en el log de auditoría antes de enviar
                await _auditService.LogOperationAsync(
                    clientId,
                    $"API-{action}",
                    endpoint,
                    data
                );
                
                // Enviar la solicitud
                HttpResponseMessage response = await _httpClient.SendAsync(request);
                
                // Leer la respuesta
                string responseContent = await response.Content.ReadAsStringAsync();
                
                // Registrar el resultado
                await _auditService.LogOperationAsync(
                    clientId,
                    $"API-Response-{action}",
                    endpoint,
                    $"Status: {response.StatusCode}, Response: {responseContent}"
                );
                
                // Asegurar que la respuesta sea exitosa
                response.EnsureSuccessStatusCode();
                
                return responseContent;
            }
            catch (Exception ex)
            {
                // Registrar el error
                await _auditService.LogOperationAsync(
                    clientId,
                    $"API-Error-{action}",
                    "api/reservations",
                    ex.Message
                );
                
                // Devolver error en formato JSON
                return JsonConvert.SerializeObject(new
                {
                    Error = true,
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// Determina el método HTTP basado en la acción solicitada
        /// </summary>
        private HttpMethod DetermineHttpMethod(string action)
        {
            return action.ToLower() switch
            {
                "create" => HttpMethod.Post,
                "update" => HttpMethod.Put,
                "delete" => HttpMethod.Delete,
                "get" or "list" => HttpMethod.Get,
                _ => HttpMethod.Get
            };
        }

        /// <summary>
        /// Determina el endpoint de la API basado en la acción solicitada
        /// </summary>
        private string DetermineEndpoint(string action)
        {
            // Por defecto usamos el endpoint de reservas
            string resourceType = "api/reservations";
            
            // Extraer el ID del recurso si está presente en los datos
            string resourceId = "";
            if (action.ToLower() == "get" || action.ToLower() == "update" || action.ToLower() == "delete")
            {
                try
                {
                    // Intentar extraer el ID del JSON
                    var data = JsonConvert.DeserializeObject<dynamic>(action.ToLower() == "get" || action.ToLower() == "delete" 
                        ? "{\"Id\":1}" // Simulación para pruebas
                        : "{\"Id\":1}"); // Simulación para pruebas
                    
                    if (data != null && data.Id != null)
                    {
                        resourceId = $"/{data.Id}";
                    }
                }
                catch
                {
                    // Si no se puede extraer el ID, usamos un endpoint genérico
                    resourceId = "";
                }
            }
            
            return $"{resourceType}{resourceId}";
        }
    }
} 