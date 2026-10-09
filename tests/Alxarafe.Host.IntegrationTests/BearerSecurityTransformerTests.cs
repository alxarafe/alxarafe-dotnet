using Alxarafe.Host;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class BearerSecurityTransformerTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task SecurityFollowsAuthorizationMetadataAndRespectsAllowAnonymous(bool authorized, bool anonymous, bool expected)
    {
        var metadata = new List<object>();
        if (authorized) metadata.Add(new AuthorizeAttribute("permission.from.any.module"));
        if (anonymous) metadata.Add(new AllowAnonymousAttribute());
        await AssertSecurityAsync(metadata, expected);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task SecurityAlsoRecognizesExplicitAuthorizationPolicyMetadata(bool anonymous, bool expected)
    {
        var metadata = new List<object> { new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build() };
        if (anonymous) metadata.Add(new AllowAnonymousAttribute());
        await AssertSecurityAsync(metadata, expected);
    }

    private static async Task AssertSecurityAsync(IList<object> metadata, bool expected)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var operation = new OpenApiOperation();
        var context = new OpenApiOperationTransformerContext
        {
            DocumentName = "v1",
            Document = new OpenApiDocument(),
            Description = new ApiDescription { ActionDescriptor = new ActionDescriptor { EndpointMetadata = metadata } },
            ApplicationServices = services
        };
        await new BearerSecurityTransformer().TransformAsync(operation, context, CancellationToken.None);
        Assert.Equal(expected, operation.Security is { Count: > 0 });
    }
}
