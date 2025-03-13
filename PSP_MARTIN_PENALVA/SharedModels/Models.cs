using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace SharedModels
{
    // Modelo para las reservas
    public class Reservation
    {
        public long Id { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public DateTime ReservationDateTime { get; set; }
        public int NumberOfGuests { get; set; }
        public string? SpecialRequests { get; set; }
        public int TableNumber { get; set; }
        public bool IsConfirmed { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastModifiedAt { get; set; }

        public override string ToString()
        {
            return $"Reserva #{Id}: {CustomerName}, {NumberOfGuests} personas, Mesa #{TableNumber}, {ReservationDateTime}";
        }
    }

    // Clase para envolver los mensajes entre cliente y servidor
    public class Message
    {
        public MessageType Type { get; set; }
        public string Action { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;

        public static Message Create(MessageType type, string action, object data, string clientId = "")
        {
            return new Message
            {
                Type = type,
                Action = action,
                Data = JsonConvert.SerializeObject(data),
                ClientId = clientId
            };
        }
    }

    // Enum para los tipos de mensaje
    public enum MessageType
    {
        Request,
        Response,
        Error,
        Notification
    }

    // Enum para las operaciones CRUD
    public enum CrudOperation
    {
        Create,
        Read,
        Update,
        Delete,
        List
    }

    // Clase para registro de operaciones
    public class AuditLog
    {
        public long Id { get; set; }
        public string ClientId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Operation { get; set; } = string.Empty;
        public string Resource { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }
} 