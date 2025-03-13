using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using SharedModels;

namespace IntermediateServer
{
    /// <summary>
    /// Servicio de criptografía para manejar cifrado asimétrico RSA en el servidor
    /// Implementa el cifrado y descifrado entre servidor y clientes
    /// </summary>
    public class CryptoService : CryptoServiceBase
    {
        private Dictionary<string, RSAParameters> _clientPublicKeys = new Dictionary<string, RSAParameters>();

        public CryptoService() : base()
        {
            Console.WriteLine("Par de claves RSA generado para el servidor.");
        }

        /// <summary>
        /// Registra una clave pública de un cliente
        /// </summary>
        public bool RegisterClientKey(string clientId, string publicKeyXml)
        {
            try
            {
                var publicKey = ImportPublicKeyFromXml(publicKeyXml);
                _clientPublicKeys[clientId] = publicKey;
                Console.WriteLine($"Clave pública registrada para el cliente {clientId}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al registrar clave del cliente: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Establece la clave pública de un cliente
        /// </summary>
        public void SetClientPublicKey(string clientId, string xmlString)
        {
            RegisterClientKey(clientId, xmlString);
        }

        /// <summary>
        /// Cifra un mensaje para un cliente específico usando su clave pública
        /// </summary>
        public string EncryptForClient(string clientId, string message)
        {
            if (!_clientPublicKeys.ContainsKey(clientId))
            {
                throw new InvalidOperationException($"No se ha registrado clave pública para el cliente {clientId}");
            }

            var data = Encoding.UTF8.GetBytes(message);
            var encryptedData = Encrypt(data, _clientPublicKeys[clientId]);
            return Convert.ToBase64String(encryptedData);
        }

        /// <summary>
        /// Verifica la firma de un mensaje de un cliente
        /// </summary>
        public bool VerifyClientSignature(string clientId, string message, string signatureBase64)
        {
            if (!_clientPublicKeys.ContainsKey(clientId))
            {
                throw new InvalidOperationException($"No se ha registrado clave pública para el cliente {clientId}");
            }

            try
            {
                var data = Encoding.UTF8.GetBytes(message);
                var signature = Convert.FromBase64String(signatureBase64);
                return VerifySignature(data, signature, _clientPublicKeys[clientId]);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al verificar firma: {ex.Message}");
                return false;
            }
        }
    }
} 