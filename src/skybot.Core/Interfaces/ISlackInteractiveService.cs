using Microsoft.AspNetCore.Http;
using skybot.Core.Models.Common;

namespace skybot.Core.Interfaces;

public interface ISlackInteractiveService
{
    Task<ServiceResult> HandleInteractiveEventAsync(HttpRequest request);
}

