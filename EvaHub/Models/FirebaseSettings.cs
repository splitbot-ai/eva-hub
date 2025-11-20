namespace EvaHub.Models;

public class FirebaseSettings
{
    public string type { get; init; } = string.Empty;
    public string project_id { get; init; } = string.Empty;
    public string private_key_id { get; init; } = string.Empty;
    public string private_key { get; init; } = string.Empty;
    public string client_email { get; init; } = string.Empty;
    public string client_id { get; init; } = string.Empty;
    public string auth_uri { get; init; } = string.Empty;
    public string token_uri { get; init; } = string.Empty;
    public string auth_provider_x509_cert_url { get; init; } = string.Empty;
    public string client_x509_cert_url { get; init; } = string.Empty;
    public string universe_domain { get; init; } = string.Empty;

}