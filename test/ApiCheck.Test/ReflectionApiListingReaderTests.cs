// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using ApiCheck.IO;

namespace ApiCheck.Test
{
    public class ReflectionApiListingReaderTests : ApiListingReaderTestBase
    {
        protected override IApiListingReader CreateReader(Assembly assembly, params Func<MemberInfo, bool>[] filters)
        {
            filters = filters ?? new Func<MemberInfo, bool>[] { };
            return new ReflectionApiListingReader(assembly, filters);
        }
    }
}
