using System;
using System.IO;
using ApiCheck.Description;
using Xunit;

namespace ApiCheck.IO
{
    public class JsonApiListingReaderTests
    {
        [Fact]
        public void LoadFromFiltersMembersOnTheInternalNamespace()
        {
            // Arrange
            var serialized = @"{
  ""AssemblyIdentity"": ""Test"",
  ""Types"": [
    {
      ""Name"": ""Scenarios.Internal.ExcludedType""
    }
  ]
}";

            // Act
            var report = new JsonApiListingReader(new StringReader(serialized), new Func<ApiElement, bool>[] { ApiListingFilters.IsInInternalNamespace }).Read();

            // Assert
            Assert.NotNull(report);
            Assert.NotNull(report.Types);
            Assert.Empty(report.Types);
        }
    }
}
