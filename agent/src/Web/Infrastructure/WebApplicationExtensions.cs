using System.Reflection;

public static class WebApplicationExtensions
{
    public static WebApplication MapEndpoints(this WebApplication app)
    {
        var endpointGroupType = typeof(EndpointGroupBase);
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var type in assembly.GetExportedTypes().Where(t => t.IsSubclassOf(endpointGroupType) && !t.IsAbstract))
        {
            if (Activator.CreateInstance(type) is EndpointGroupBase instance)
            {
                var groupName = instance.GroupName ?? type.Name;
                instance.Map(app.MapGroup($"/api/{groupName}").WithTags(groupName));
            }
        }

        return app;
    }
}