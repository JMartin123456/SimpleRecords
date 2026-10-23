using System.ComponentModel.DataAnnotations;

namespace SimpleRecords.Server.Models;

public class Position
{
    public int PositionID { get; set; }

    [Required]
    [MaxLength(100)]
    public string PositionName { get; set; } = string.Empty;
}
