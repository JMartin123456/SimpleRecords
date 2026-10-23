
using System.ComponentModel.DataAnnotations;

namespace SimpleRecords.Server.Models;

public class Employee
{
    public int EmployeeID { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Surname { get; set; } = string.Empty;

    [Required]
    public DateTime BirthDate { get; set; }

    // Nullable: pri importe môže pracovná pozícia chýbať.
    public int? PositionID { get; set; }

    public Position? Position { get; set; }

    [Required]
    [MaxLength(45)]
    public string IPaddress { get; set; } = string.Empty;

    [MaxLength(2)]
    public string? IPCountryCode { get; set; }
}
