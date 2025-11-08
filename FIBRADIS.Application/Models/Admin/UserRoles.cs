namespace FIBRADIS.Application.Models.Admin;

public static class UserRoles
{
    public const string Viewer = "viewer";
    public const string User = "user";
    public const string Admin = "admin";

    public static bool IsValid(string role)
    {
        return role is Viewer or User or Admin;
    }
}
