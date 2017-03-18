// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using ApiCheck.Description;

namespace ApiCheck.IO
{
    public class DllApiListingReader : IApiListingReader
    {
        private readonly MetadataReader _reader;

        public PeApiListingReader(Stream file)
        {
            _reader = MetadataReaderProvider
                .FromPortablePdbStream(file)
                .GetMetadataReader(MetadataReaderOptions.Default);
        }

        public ApiListing Read()
        {
            var listing = new ApiListing();

            var def = _reader.GetAssemblyDefinition();
            var name = new AssemblyName
            {
                Name = _reader.GetString(def.Name),
                CultureName = _reader.GetString(def.Culture),
                Version = def.Version
            };
            name.SetPublicKey(_reader.GetBlobBytes(def.PublicKey));
            listing.AssemblyIdentity = name.ToString();

            foreach (var handle in _reader.TypeDefinitions)
            {
                var type = _reader.GetTypeDefinition(handle);
                var descriptor = new TypeDescriptor();
                descriptor
                listing.Types.Add()
            }

            return listing;
        }

        public void Dispose()
        {
        }
    }
}
