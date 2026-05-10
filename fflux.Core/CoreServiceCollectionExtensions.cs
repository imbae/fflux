using Microsoft.Extensions.DependencyInjection;

namespace fflux.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // Phase 2 이후 FFmpegInitializer, MediaFileReader, VideoDecoder 등 등록 예정
        return services;
    }
}
