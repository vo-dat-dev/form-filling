using System.Diagnostics.CodeAnalysis;

public static class EndpointRouteBuilderExtensions
{
    public static RouteHandlerBuilder MapGet(this IEndpointRouteBuilder builder, Delegate handler, [StringSyntax("Route")] string pattern = "")
        => builder.MapGet(pattern, handler).WithName(handler.Method.Name);

    public static RouteHandlerBuilder MapPost(this IEndpointRouteBuilder builder, Delegate handler, [StringSyntax("Route")] string pattern = "")
        => builder.MapPost(pattern, handler).WithName(handler.Method.Name);

    public static RouteHandlerBuilder MapPut(this IEndpointRouteBuilder builder, Delegate handler, [StringSyntax("Route")] string pattern = "")
        => builder.MapPut(pattern, handler).WithName(handler.Method.Name);

    public static RouteHandlerBuilder MapPatch(this IEndpointRouteBuilder builder, Delegate handler, [StringSyntax("Route")] string pattern = "")
        => builder.MapPatch(pattern, handler).WithName(handler.Method.Name);

    public static RouteHandlerBuilder MapDelete(this IEndpointRouteBuilder builder, Delegate handler, [StringSyntax("Route")] string pattern = "")
        => builder.MapDelete(pattern, handler).WithName(handler.Method.Name);
}