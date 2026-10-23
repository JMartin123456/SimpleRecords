
using System.ComponentModel.DataAnnotations;

namespace SimpleRecords.Client.Models;

public class Employee
{
    public int EmployeeID { get; set; }

    [Required(ErrorMessage = "Meno je povinné.")]
    [MaxLength(100, ErrorMessage = "Meno môže mať najviac 100 znakov.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Priezvisko je povinné.")]
    [MaxLength(100, ErrorMessage = "Priezvisko môže mať najviac 100 znakov.")]
    public string Surname { get; set; } = string.Empty;

    [Required(ErrorMessage = "Dátum narodenia je povinný.")]
    [DataType(DataType.Date)]
    public DateTime? BirthDate { get; set; }

    [Required(ErrorMessage = "Vyber pracovnú pozíciu.")]
    public int? PositionID { get; set; }

    public string? PositionName { get; set; }

    [Required(ErrorMessage = "IP adresa je povinná.")]
    [MaxLength(45, ErrorMessage = "IP adresa môže mať najviac 45 znakov.")]
    public string IPaddress { get; set; } = string.Empty;

    public string? IPCountryCode { get; set; }
}
