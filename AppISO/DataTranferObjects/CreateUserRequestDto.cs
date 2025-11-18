namespace AppISO.DataTranferObjects
{
    public record CreateUserRequestDto(
        string UserName,
        string Password,
        string Role
    );
}
