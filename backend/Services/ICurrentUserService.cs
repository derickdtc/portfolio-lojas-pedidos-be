namespace backend.Services;

public interface ICurrentUserService
{
    int GetUserId();

    string GetUsername();

    int GetCurrentStoreId();

    string GetRole();
}
