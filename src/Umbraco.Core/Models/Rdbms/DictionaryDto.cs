﻿using System;
using System.Collections.Generic;
using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.DatabaseAnnotations;

namespace Umbraco.Core.Models.Rdbms
{
    [TableName("cmsDictionary")]
    [PrimaryKey("pk")]
    [ExplicitColumns]
    internal class DictionaryDto
    {
        [Column("pk")]
        [PrimaryKeyColumn]
        public int PrimaryKey { get; set; }

        [Column("id")]
        [Index(IndexTypes.UniqueNonClustered)]
        public Guid Id { get; set; }

        [Column("parent")]
        public Guid Parent { get; set; }

        [Column("key")]
        [DatabaseType(SpecialDbTypes.NVARCHAR, Length = 1000)]
        public string Key { get; set; }

        [ResultColumn]
        public List<LanguageTextDto> LanguageTextDtos { get; set; }
    }
}