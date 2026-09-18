namespace Frpm.Abstractions.Services;

[RemoteService("/api/user-preferences")]
[RemoteAuthorize]
public interface IUserPreferencesService
{
    [Get]
    Task<UserPreferencesModel> GetAsync(CancellationToken cancellationToken = default);

    [Put]
    Task<UserPreferencesModel> SaveAsync(
        SaveUserPreferencesRequest request,
        CancellationToken cancellationToken = default);
}
