using System.Security.Cryptography;
using System.Text;

namespace RainWorldConnect.Data {
    /// <summary>
    /// 跨平台非对称加密工具类
    /// 不依赖外部存储机制，密钥管理由调用方负责
    /// </summary>
    public partial class AsymmetricCryptoHelper : IDisposable {
        private readonly RSA _rsa = RSA.Create();
        private bool disposedValue;

        /// <summary>
        /// 导入公钥
        /// </summary>
        /// <param name="publicKeyXml">公钥XML字符串</param>
        public void ImportPublicKey(string publicKeyXml) {
            if (string.IsNullOrEmpty(publicKeyXml))
                throw new ArgumentException("Public key cannot be null or empty", nameof(publicKeyXml));

            _rsa.FromXmlString(publicKeyXml);
        }

        /// <summary>
        /// 导入私钥
        /// </summary>
        /// <param name="privateKeyXml">私钥XML字符串</param>
        public void ImportPrivateKey(string privateKeyXml) {
            if (string.IsNullOrEmpty(privateKeyXml))
                throw new ArgumentException("Private key cannot be null or empty", nameof(privateKeyXml));

            _rsa.FromXmlString(privateKeyXml);
        }

        /// <summary>
        /// 导出公钥到PEM格式
        /// </summary>
        /// <returns>PEM格式的公钥字符串</returns>
        public string ExportPublicKeyToPem() {
            byte[] publicKeyBytes = _rsa.ExportSubjectPublicKeyInfo();
            return Convert.ToBase64String(publicKeyBytes);
        }

        /// <summary>
        /// 导出私钥到PEM格式
        /// </summary>
        /// <returns>PEM格式的私钥字符串</returns>
        public string ExportPrivateKeyToPem() {
            byte[] privateKeyBytes = _rsa.ExportPkcs8PrivateKey();
            return Convert.ToBase64String(privateKeyBytes);
        }

        /// <summary>
        /// 从PEM格式导入公钥
        /// </summary>
        /// <param name="pemPublicKey">PEM格式的公钥字符串</param>
        public void ImportPublicKeyFromPem(string pemPublicKey) {
            if (string.IsNullOrEmpty(pemPublicKey))
                throw new ArgumentException("PEM public key cannot be null or empty", nameof(pemPublicKey));

            byte[] publicKeyBytes = Convert.FromBase64String(pemPublicKey);
            _rsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);
        }

        /// <summary>
        /// 从PEM格式导入私钥
        /// </summary>
        /// <param name="pemPrivateKey">PEM格式的私钥字符串</param>
        public void ImportPrivateKeyFromPem(string pemPrivateKey) {
            if (string.IsNullOrEmpty(pemPrivateKey))
                throw new ArgumentException("PEM private key cannot be null or empty", nameof(pemPrivateKey));

            byte[] privateKeyBytes = Convert.FromBase64String(pemPrivateKey);
            _rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
        }

        /// <summary>
        /// 加密数据
        /// </summary>
        /// <param name="data">要加密的数据</param>
        /// <returns>加密后的Base64字符串</returns>
        public string Encrypt(string data) {
            if (string.IsNullOrEmpty(data))
                throw new ArgumentException("Data cannot be null or empty", nameof(data));

            var dataBytes = Encoding.UTF8.GetBytes(data);
            var encryptedBytes = _rsa.Encrypt(dataBytes, RSAEncryptionPadding.OaepSHA256);

            return Convert.ToBase64String(encryptedBytes);
        }

        /// <summary>
        /// 解密数据
        /// </summary>
        /// <param name="encryptedData">加密的Base64字符串</param>
        /// <returns>解密后的原始数据</returns>
        public string Decrypt(string encryptedData) {
            if (string.IsNullOrEmpty(encryptedData))
                throw new ArgumentException("Encrypted data cannot be null or empty", nameof(encryptedData));

            var encryptedBytes = Convert.FromBase64String(encryptedData);
            var decryptedBytes = _rsa.Decrypt(encryptedBytes, RSAEncryptionPadding.OaepSHA256);

            return Encoding.UTF8.GetString(decryptedBytes);
        }

        /// <summary>
        /// 加密数据（字节数组版本）
        /// </summary>
        /// <param name="data">要加密的数据字节数组</param>
        /// <returns>加密后的字节数组</returns>
        public byte[] Encrypt(byte[] data) {
            if (data == null || data.Length == 0)
                throw new ArgumentException("Data cannot be null or empty", nameof(data));

            return _rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
        }

        /// <summary>
        /// 解密数据（字节数组版本）
        /// </summary>
        /// <param name="encryptedData">加密的数据字节数组</param>
        /// <returns>解密后的字节数组</returns>
        public byte[] Decrypt(byte[] encryptedData) {
            if (encryptedData == null || encryptedData.Length == 0)
                throw new ArgumentException("Encrypted data cannot be null or empty", nameof(encryptedData));

            return _rsa.Decrypt(encryptedData, RSAEncryptionPadding.OaepSHA256);
        }

        /// <summary>
        /// 检查是否包含私钥
        /// </summary>
        /// <returns>如果包含私钥返回true，否则返回false</returns>
        public bool HasPrivateKey() {
            try {
                // 尝试导出私钥，如果失败则说明没有私钥
                _rsa.ToXmlString(true);
                return true;
            } catch {
                return false;
            }
        }

        /// <summary>
        /// 创建数字签名
        /// </summary>
        /// <param name="data">要签名的数据</param>
        /// <returns>Base64编码的签名</returns>
        public string SignData(string data) {
            if (string.IsNullOrEmpty(data))
                throw new ArgumentException("Data cannot be null or empty", nameof(data));

            var dataBytes = Encoding.UTF8.GetBytes(data);
            var signature = _rsa.SignData(dataBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            return Convert.ToBase64String(signature);
        }

        /// <summary>
        /// 验证数字签名
        /// </summary>
        /// <param name="data">原始数据</param>
        /// <param name="signature">Base64编码的签名</param>
        /// <returns>如果验证成功返回true，否则返回false</returns>
        public bool VerifyData(string data, string signature) {
            if (string.IsNullOrEmpty(data))
                throw new ArgumentException("Data cannot be null or empty", nameof(data));

            if (string.IsNullOrEmpty(signature))
                throw new ArgumentException("Signature cannot be null or empty", nameof(signature));

            var dataBytes = Encoding.UTF8.GetBytes(data);
            var signatureBytes = Convert.FromBase64String(signature);

            return _rsa.VerifyData(dataBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    // TODO: 释放托管状态(托管对象)
                    _rsa.Dispose();
                }

                // TODO: 释放未托管的资源(未托管的对象)并重写终结器
                // TODO: 将大型字段设置为 null
                disposedValue = true;
            }
        }

        // // TODO: 仅当“Dispose(bool disposing)”拥有用于释放未托管资源的代码时才替代终结器
        // ~AsymmetricCryptoHelper()
        // {
        //     // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
        //     Dispose(disposing: false);
        // }

        public void Dispose() {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}