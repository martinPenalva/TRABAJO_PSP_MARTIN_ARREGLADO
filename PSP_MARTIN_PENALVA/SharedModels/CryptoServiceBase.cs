using System;
using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace SharedModels
{
    /// <summary>
    /// Clase base para servicios de criptografía que proporciona funcionalidad común
    /// para cifrado y descifrado con RSA
    /// </summary>
    public abstract class CryptoServiceBase
    {
        protected RSA _privateKey;
        protected RSA _publicKey;

        protected CryptoServiceBase()
        {
            // Generar par de claves
            _privateKey = RSA.Create(2048);
            _publicKey = RSA.Create();
            
            // Extraer clave pública
            RSAParameters publicKeyParams = _privateKey.ExportParameters(false);
            _publicKey.ImportParameters(publicKeyParams);
        }

        /// <summary>
        /// Obtiene la clave pública en formato XML
        /// </summary>
        public string GetPublicKeyXml()
        {
            var sw = new StringWriter();
            var xs = new System.Xml.Serialization.XmlSerializer(typeof(RSAParameters));
            xs.Serialize(sw, _privateKey.ExportParameters(false));
            return sw.ToString();
        }

        /// <summary>
        /// Importa una clave pública desde formato XML
        /// </summary>
        public RSAParameters ImportPublicKeyFromXml(string xmlString)
        {
            var sr = new StringReader(xmlString);
            var xs = new System.Xml.Serialization.XmlSerializer(typeof(RSAParameters));
            return (RSAParameters)xs.Deserialize(sr);
        }

        /// <summary>
        /// Cifra datos con una clave pública específica
        /// </summary>
        public byte[] Encrypt(byte[] data, RSAParameters publicKey)
        {
            var rsa = RSA.Create();
            rsa.ImportParameters(publicKey);
            
            int maxDataLength = (rsa.KeySize / 8) - 42; // Tamaño máximo que puede cifrarse con RSA-OAEP
            
            // Si los datos son demasiado largos, ciframos en bloques
            if (data.Length > maxDataLength)
            {
                using (var aes = Aes.Create())
                {
                    aes.GenerateKey();
                    aes.GenerateIV();
                    
                    // Ciframos la clave AES con RSA
                    byte[] encryptedAesKey = rsa.Encrypt(aes.Key, RSAEncryptionPadding.OaepSHA256);
                    byte[] encryptedAesIV = rsa.Encrypt(aes.IV, RSAEncryptionPadding.OaepSHA256);
                    
                    // Ciframos los datos con AES
                    using (var encryptor = aes.CreateEncryptor())
                    using (var ms = new MemoryStream())
                    {
                        // Formato: [Longitud de clave AES cifrada][Clave AES cifrada][Longitud de IV cifrado][IV cifrado][Datos cifrados con AES]
                        byte[] keyLengthBytes = BitConverter.GetBytes(encryptedAesKey.Length);
                        byte[] ivLengthBytes = BitConverter.GetBytes(encryptedAesIV.Length);
                        
                        ms.Write(keyLengthBytes, 0, keyLengthBytes.Length);
                        ms.Write(encryptedAesKey, 0, encryptedAesKey.Length);
                        ms.Write(ivLengthBytes, 0, ivLengthBytes.Length);
                        ms.Write(encryptedAesIV, 0, encryptedAesIV.Length);
                        
                        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                        {
                            cs.Write(data, 0, data.Length);
                            cs.FlushFinalBlock();
                        }
                        
                        return ms.ToArray();
                    }
                }
            }
            else
            {
                // Ciframos directamente con RSA si es posible
                return rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
            }
        }

        /// <summary>
        /// Descifra datos usando la clave privada local
        /// </summary>
        public byte[] Decrypt(byte[] encryptedData)
        {
            try
            {
                // Intentamos descifrar directamente con RSA
                return _privateKey.Decrypt(encryptedData, RSAEncryptionPadding.OaepSHA256);
            }
            catch
            {
                // Si falla, asumimos que está cifrado en formato híbrido RSA+AES
                using (var ms = new MemoryStream(encryptedData))
                using (var reader = new BinaryReader(ms))
                {
                    // Leer la longitud de la clave AES cifrada
                    int keyLength = reader.ReadInt32();
                    byte[] encryptedAesKey = reader.ReadBytes(keyLength);
                    
                    // Leer la longitud del IV cifrado
                    int ivLength = reader.ReadInt32();
                    byte[] encryptedAesIV = reader.ReadBytes(ivLength);
                    
                    // Descifrar la clave y el IV de AES
                    byte[] aesKey = _privateKey.Decrypt(encryptedAesKey, RSAEncryptionPadding.OaepSHA256);
                    byte[] aesIV = _privateKey.Decrypt(encryptedAesIV, RSAEncryptionPadding.OaepSHA256);
                    
                    // Leer los datos cifrados con AES
                    byte[] encryptedContent = reader.ReadBytes((int)(ms.Length - ms.Position));
                    
                    // Descifrar los datos con AES
                    using (var aes = Aes.Create())
                    {
                        aes.Key = aesKey;
                        aes.IV = aesIV;
                        
                        using (var decryptor = aes.CreateDecryptor())
                        using (var resultStream = new MemoryStream())
                        {
                            using (var cs = new CryptoStream(resultStream, decryptor, CryptoStreamMode.Write))
                            {
                                cs.Write(encryptedContent, 0, encryptedContent.Length);
                                cs.FlushFinalBlock();
                            }
                            
                            return resultStream.ToArray();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Firma datos usando la clave privada local
        /// </summary>
        public byte[] SignData(byte[] data)
        {
            return _privateKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        /// <summary>
        /// Firma un mensaje de texto usando la clave privada local
        /// </summary>
        public string SignMessage(string message)
        {
            var data = Encoding.UTF8.GetBytes(message);
            var signature = SignData(data);
            return Convert.ToBase64String(signature);
        }

        /// <summary>
        /// Verifica la firma de unos datos usando una clave pública
        /// </summary>
        public bool VerifySignature(byte[] data, byte[] signature, RSAParameters publicKey)
        {
            var rsa = RSA.Create();
            rsa.ImportParameters(publicKey);
            return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    }
} 