using Deutchezz.Domain.Common;

namespace Deutchezz.Domain.Entities;

public class Setting : AuditableEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}