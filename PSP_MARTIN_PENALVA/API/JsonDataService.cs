using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using SharedModels;

/// <summary>
/// Servicio para gestionar la persistencia de datos en JSON
/// Implementa operaciones CRUD con bloqueo para acceso concurrente
/// </summary>
public class JsonDataService
{
    private readonly string _jsonFilePath;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private long _nextId = 1;

    public JsonDataService(string jsonFilePath)
    {
        _jsonFilePath = jsonFilePath;
        
        // Crear directorio si no existe
        var directory = Path.GetDirectoryName(jsonFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        // Crear archivo si no existe
        if (!File.Exists(jsonFilePath))
        {
            SaveData(new List<Reservation>());
        }
        
        // Obtener el mayor ID de las reservas existentes
        var reservations = LoadData();
        if (reservations.Count > 0)
        {
            _nextId = reservations.Max(r => r.Id) + 1;
        }
    }

    /// <summary>
    /// Carga los datos del archivo JSON
    /// </summary>
    public List<Reservation> LoadData()
    {
        try
        {
            if (!File.Exists(_jsonFilePath))
                return new List<Reservation>();

            var json = File.ReadAllText(_jsonFilePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            return string.IsNullOrWhiteSpace(json) 
                ? new List<Reservation>() 
                : JsonSerializer.Deserialize<List<Reservation>>(json, options) ?? new List<Reservation>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al cargar datos JSON: {ex.Message}");
            return new List<Reservation>();
        }
    }

    /// <summary>
    /// Guarda los datos en el archivo JSON
    /// </summary>
    private void SaveData(List<Reservation> reservations)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            
            var json = JsonSerializer.Serialize(reservations, options);
            File.WriteAllText(_jsonFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al guardar datos JSON: {ex.Message}");
        }
    }

    /// <summary>
    /// Obtiene todas las reservas
    /// </summary>
    public async Task<List<Reservation>> GetAllReservationsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return LoadData();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Obtiene una reserva por su ID
    /// </summary>
    public async Task<Reservation?> GetReservationByIdAsync(long id)
    {
        await _lock.WaitAsync();
        try
        {
            var reservations = LoadData();
            return reservations.FirstOrDefault(r => r.Id == id);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Crea una nueva reserva
    /// </summary>
    public async Task<Reservation> CreateReservationAsync(Reservation reservation)
    {
        await _lock.WaitAsync();
        try
        {
            var reservations = LoadData();
            
            // Check for existing reservation with the same date, time, and table
            var conflict = reservations.Any(r => r.ReservationDateTime == reservation.ReservationDateTime && r.TableNumber == reservation.TableNumber);
            if (conflict)
            {
                throw new InvalidOperationException("La mesa ya está reservada para la fecha y hora especificadas.");
            }

            // Asignar ID y fecha de creación
            reservation.Id = _nextId++;
            reservation.CreatedAt = DateTime.Now;
            
            // Agregar a la lista
            reservations.Add(reservation);
            
            // Guardar cambios
            SaveData(reservations);
            
            return reservation;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Actualiza una reserva existente
    /// </summary>
    public async Task<Reservation?> UpdateReservationAsync(long id, Reservation updatedReservation)
    {
        await _lock.WaitAsync();
        try
        {
            var reservations = LoadData();
            var index = reservations.FindIndex(r => r.Id == id);
            
            if (index == -1)
                return null;
            
            // Check for conflicting reservations (same date, time, and table)
            var conflict = reservations.Any(r => 
                r.Id != id && // Exclude the current reservation
                r.ReservationDateTime == updatedReservation.ReservationDateTime && 
                r.TableNumber == updatedReservation.TableNumber);
                
            if (conflict)
            {
                throw new InvalidOperationException("La mesa ya está reservada para la fecha y hora especificadas.");
            }
            
            // Mantener el ID original y actualizar la fecha de modificación
            updatedReservation.Id = id;
            updatedReservation.LastModifiedAt = DateTime.Now;
            
            // Actualizar en la lista
            reservations[index] = updatedReservation;
            
            // Guardar cambios
            SaveData(reservations);
            
            return updatedReservation;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Elimina una reserva
    /// </summary>
    public async Task<bool> DeleteReservationAsync(long id)
    {
        await _lock.WaitAsync();
        try
        {
            var reservations = LoadData();
            var index = reservations.FindIndex(r => r.Id == id);
            
            if (index == -1)
                return false;
            
            // Eliminar de la lista
            reservations.RemoveAt(index);
            
            // Guardar cambios
            SaveData(reservations);
            
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }
} 