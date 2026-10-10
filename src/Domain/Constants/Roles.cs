namespace Deutchezz.Domain.Constants;

public static class Roles
{
    public const string Administrator = "Administrator";
    public const string Receptionist = "Receptionist";
    public const string Customer = "Customer";

    public const string Staff = Receptionist + "," + Administrator;
}
