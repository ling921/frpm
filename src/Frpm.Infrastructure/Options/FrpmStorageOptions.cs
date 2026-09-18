namespace Frpm.Infrastructure.Options;

public sealed class FrpmStorageOptions
{
    public const string SectionName = "Frpm:Storage";
    public string DataDirectory { get; set; } = "data";
    public int LogRetentionDays { get; set; } = 30;
    public long MaxLogBytes { get; set; } = 536_870_912;
    public long MaxPackageBytes { get; set; } = 268_435_456;
}
