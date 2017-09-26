using System;
using System.IO;
using System.Reflection;
using ApiCheck.IO;

namespace ApiCheck.Test
{
    public class PortableExecutableApiListingReaderTests : ApiListingReaderTestBase
    {
        protected override IApiListingReader CreateReader(Assembly assembly, params Func<MemberInfo, bool>[] filters)
        {
            var fileStream = File.OpenRead(assembly.Location);
            return new PortableExecutableApiListingReader(fileStream, keepOpen: false);
        }
    }
}
