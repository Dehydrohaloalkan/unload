namespace Unload.Api.UseCases;

public  class GetServerTimeUseCase : IGetServerTimeUseCase
{
    public ServerTimeResponse Execute()
    {
        var localNow = DateTimeOffset.Now;
        return new ServerTimeResponse(
            ServerLocalTime: localNow,
            ServerUtcTime: localNow.ToUniversalTime(),
            UtcOffsetMinutes: (int)localNow.Offset.TotalMinutes,
            TimeZoneId: TimeZoneInfo.Local.Id);
    }
}
