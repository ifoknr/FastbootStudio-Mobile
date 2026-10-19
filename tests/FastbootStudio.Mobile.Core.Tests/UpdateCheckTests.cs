using Xunit;

namespace FastbootStudio.Mobile.Core.Tests
{
    public class UpdateCheckTests
    {
        [Theory]
        [InlineData("1.0.1", "1.0.0", true)]
        [InlineData("v1.0.10", "1.0.9", true)]
        [InlineData("1.1", "1.0.9", true)]
        [InlineData("1.0.0", "1.0.0", false)]
        [InlineData("1.0.0", "1.0.1", false)]
        [InlineData("2.0.0-beta", "1.9.9", true)]
        [InlineData("latest", "1.0.0", false)]
        [InlineData("1.0.1", null, false)]
        [InlineData("1.0.0", "1.0.0-beta.1", true)]
        [InlineData("1.0.0-beta.2", "1.0.0-beta.1", true)]
        [InlineData("1.0.0-beta.10", "1.0.0-beta.9", true)]
        [InlineData("1.0.0-beta.1", "1.0.0", false)]
        [InlineData("1.0.0-beta.1", "1.0.0-beta.1", false)]
        public void Versions_compare_by_their_numbers(string latest, string installed, bool newer) =>
            Assert.Equal(newer, UpdateCheck.IsNewer(latest, installed));

        [Fact]
        public void A_release_is_read_and_its_page_is_always_the_project_s()
        {
            UpdateCheck.Release r = UpdateCheck.Parse("{\"tag_name\":\"v1.0.1\",\"html_url\":\"https://github.com/ifoknr/FastbootStudio-Mobile/releases/tag/v1.0.1\",\"draft\":false,"
                + "\"assets\":[{\"name\":\"SHA256SUMS\",\"size\":90},{\"name\":\"FastbootStudio-Mobile-1.0.1.apk\",\"size\":10973728}]}");
            Assert.Equal("1.0.1", r.Version);
            Assert.Equal("https://github.com/ifoknr/FastbootStudio-Mobile/releases/tag/v1.0.1", r.Page);
            Assert.Equal("FastbootStudio-Mobile-1.0.1.apk", r.Apk);
            Assert.Equal(10973728, r.ApkBytes);

            Assert.Equal(UpdateCheck.Releases, UpdateCheck.Parse("{\"tag_name\":\"v1.0.1\",\"html_url\":\"https://evil.example/x\"}").Page);
        }

        [Theory]
        [InlineData("{\"message\":\"Not Found\"}")]
        [InlineData("{\"tag_name\":\"v1.0.1\",\"draft\":true}")]
        [InlineData("{\"tag_name\":\"nightly\"}")]
        [InlineData("not json")]
        [InlineData("")]
        public void Anything_else_is_no_release(string json) => Assert.Null(UpdateCheck.Parse(json));
    
        const string List = "[{\"tag_name\":\"v1.0.0-beta.2\",\"prerelease\":true,\"html_url\":\"https://github.com/ifoknr/FastbootStudio-Mobile/releases/tag/v1.0.0-beta.2\"},"
            + "{\"tag_name\":\"v0.9.0\",\"prerelease\":false},"
            + "{\"tag_name\":\"v1.0.1\",\"draft\":true},"
            + "{\"tag_name\":\"v1.0.0-beta.1\",\"prerelease\":true}]";

        [Fact]
        public void A_beta_is_offered_the_newest_beta() =>
            Assert.Equal("1.0.0-beta.2", UpdateCheck.Newest(List, "1.0.0-beta.1").Version);

        [Fact]
        public void A_release_is_never_offered_a_beta() =>
            Assert.Equal("0.9.0", UpdateCheck.Newest(List, "1.0.0").Version);

        [Fact]
        public void Drafts_never_count_and_an_empty_list_is_none()
        {
            Assert.Null(UpdateCheck.Newest("[{\"tag_name\":\"v9.0.0\",\"draft\":true}]", "1.0.0-beta.1"));
            Assert.Null(UpdateCheck.Newest("[]", "1.0.0"));
            Assert.Null(UpdateCheck.Newest("nope", "1.0.0"));
        }

        [Fact]
        public void A_beta_tag_counts_as_a_beta_even_if_not_marked() =>
            Assert.Equal("0.9.0", UpdateCheck.Newest("[{\"tag_name\":\"v2.0.0-beta.1\"},{\"tag_name\":\"v0.9.0\"}]", "0.8.0").Version);
    }
}
