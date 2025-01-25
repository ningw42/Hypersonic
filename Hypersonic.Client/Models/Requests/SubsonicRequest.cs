using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Hypersonic.Client.Models.Requests;

public class SubsonicRequest
{
    public string User { get; private set; }

    public string Password { get; private set; }

    public string ClientVersion { get; private set; }

    public string ClientId { get; private set; }

    public string Token
    {
        get
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(this.Password + this.Salt);
                return Convert.ToHexString(md5.ComputeHash(bytes)).ToLower();
            }
        }
    }

    private string salt = string.Empty;

    public string Salt 
    {
        get
        {
            if (string.IsNullOrEmpty(this.salt))
            {
                const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                Random random = new Random();

                this.salt = new string(Enumerable.Repeat(chars, Constants.SaltLength)
                    .Select(s => s[random.Next(s.Length)]).ToArray());
            }

            return this.salt;
        }
    }

    public ResponseFormat ResponseFormat { get; private set; }

    public virtual string Action => string.Empty;

    public SubsonicRequest(string user, string password, string clientId, string clientVersion, ResponseFormat responseFormat)
    {
        this.User = user;
        this.Password = password;
        this.ClientId = clientId;
        this.ClientVersion = clientVersion;
        this.ResponseFormat = responseFormat;
    }

    public SubsonicRequest(SubsonicRequest other)
    {
        this.User = other.User;
        this.Password = other.Password;
        this.ClientId = other.ClientId;
        this.ClientVersion = other.ClientVersion;
        this.ResponseFormat = other.ResponseFormat;
    }

    public virtual string ToQueryString()
    {
        string format = Enum.GetName(this.ResponseFormat)!.ToLower();
        return $"?u={this.User}&t={this.Token}&s={this.Salt}&v={this.ClientVersion}&c={this.ClientId}&f={format}";
    }
}
