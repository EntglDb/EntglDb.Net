using System.Threading.Tasks;

namespace EntglDb.Network.Security;

public interface IAuthenticator
{
    Task<bool> ValidateAsync(string nodeId, string token);
}
