using System;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using SharedModels;

namespace Client
{
    /// <summary>
    /// Servicio de criptografía para manejar cifrado asimétrico RSA
    /// Implementa el cifrado y descifrado entre cliente y servidor
    /// </summary>
    public class CryptoService : CryptoServiceBase
    {
        private RSAParameters _serverPublicKey;
        private bool _hasServerKey = false;

        public CryptoService() : base()
        {
            Console.WriteLine("Par de claves RSA generado para el cliente.");
        }

        /// <summary>
        /// Establece la clave pública del servidor para cifrar los mensajes
        /// </summary>
        public bool SetServerPublicKey(string xmlString)
        {
            try
            {
                _serverPublicKey = ImportPublicKeyFromXml(xmlString);
                _hasServerKey = true;
                Console.WriteLine("Clave pública del servidor importada correctamente.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al importar la clave pública del servidor: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Cifra un mensaje para el servidor usando su clave pública
        /// </summary>
        public string EncryptForServer(string message)
        {
            if (!_hasServerKey)
            {
                throw new InvalidOperationException("No se ha establecido la clave pública del servidor.");
            }

            var data = Encoding.UTF8.GetBytes(message);
            var encryptedData = Encrypt(data, _serverPublicKey);
            return Convert.ToBase64String(encryptedData);
        }

        /// <summary>
        /// Verifica la firma de un mensaje del servidor
        /// </summary>
        public bool VerifyServerSignature(string message, string signatureBase64)
        {
            if (!_hasServerKey)
            {
                throw new InvalidOperationException("No se ha establecido la clave pública del servidor.");
            }

            try
            {
                var data = Encoding.UTF8.GetBytes(message);
                var signature = Convert.FromBase64String(signatureBase64);
                return VerifySignature(data, signature, _serverPublicKey);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al verificar firma del servidor: {ex.Message}");
                return false;
            }
        }
    }
} 