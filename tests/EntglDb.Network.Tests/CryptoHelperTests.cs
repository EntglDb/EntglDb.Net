using System.Security.Cryptography;
using EntglDb.Network.Security;
using FluentAssertions;
using Xunit;

namespace EntglDb.Network.Tests
{
    public class CryptoHelperTests
    {
        [Fact]
        public void EncryptDecrypt_ShouldPreserveData()
        {
            // Arrange
            var key = new byte[32]; // 256 bits
            RandomNumberGenerator.Fill(key);
            
            var original = new byte[] { 1, 2, 3, 4, 5, 255, 0, 10 };

            // Act
            var (ciphertext, iv, tag) = CryptoHelper.Encrypt(original, key);
            var decrypted = CryptoHelper.Decrypt(ciphertext, iv, tag, key);

            // Assert
            decrypted.Should().Equal(original);
        }

        [Fact]
        public void Decrypt_ShouldFail_IfTampered()
        {
            // Arrange
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var original = new byte[] { 1, 2, 3 };
            var (ciphertext, iv, tag) = CryptoHelper.Encrypt(original, key);

            // Tamper ciphertext
            ciphertext[0] ^= 0xFF;

            // Act
            Action act = () => CryptoHelper.Decrypt(ciphertext, iv, tag, key);

            // Assert
            act.Should().Throw<CryptographicException>();
        }
    }
}
