namespace AppISO.DataTranferObjects
{
    public record LoginRequestDto(
        string UserName,
        string Password
    );
}
