using Xunit;

namespace Tjb.UiTests.Fixtures;

/// <summary>
/// Shares the suite-level <see cref="OAuthTokenFixture"/> and <see cref="AppReadinessFixture"/>
/// across every test class. Each fixture's <c>InitializeAsync</c> runs ONCE before any test in
/// the collection — token refresh ~1s, app warmup 2s-5min — and is reused thereafter.
///
/// Every UI test class must carry <c>[Collection("UiTests")]</c> to join this collection.
/// </summary>
[CollectionDefinition("UiTests")]
public class UiTestCollection :
    ICollectionFixture<OAuthTokenFixture>,
    ICollectionFixture<AppReadinessFixture>
{
}
