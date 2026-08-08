namespace ObdWebApp.Models;

/// <summary>고장코드 한 건 (코드 + 한글 설명).</summary>
public sealed record DiagnosticTroubleCode(string Code, string Description);
