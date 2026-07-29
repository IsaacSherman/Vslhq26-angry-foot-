namespace AngryFoot.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
    public const string Name = "Integration";
}

public sealed class IntegrationTestFixture
{
}
