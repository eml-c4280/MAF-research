namespace AgentCore.Application.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HMAC-SHA256 signing key - must be at least 32 bytes. Demo default, override via
    /// config/env in any real deployment (docs/plan.md section 5).</summary>
    public string SigningKey { get; set; } = "agentcore-demo-signing-key-change-me-3f8a91b2";

    public string Issuer { get; set; } = "AgentCore";
    public string Audience { get; set; } = "AgentCoreClients";

    /// <summary>No refresh tokens (docs/plan.md section 5) - one access token, this long.</summary>
    public int ExpiryMinutes { get; set; } = 480;
}
