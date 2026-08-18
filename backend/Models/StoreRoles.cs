namespace backend.Models;

public static class StoreRoles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Employee = "Employee";

    public static bool CanManageStore(string role)
    {
        return string.Equals(role, Owner, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase);
    }
}
