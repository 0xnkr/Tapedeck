using Tapedeck.Core;

namespace Tapedeck.Tests;

public sealed class LicenseTextTests
{
    [Fact]
    public void Lines_of_a_paragraph_are_joined_and_paragraphs_stay_apart()
    {
        const string TEXT = "MIT License\n\nPermission is hereby granted, free of charge, to any\nperson obtaining a copy of\nthis software.\n\nTHE SOFTWARE IS PROVIDED\n\"AS IS\".";

        Assert.Equal("MIT License\n\nPermission is hereby granted, free of charge, to any person obtaining a copy of this software.\n\nTHE SOFTWARE IS PROVIDED \"AS IS\".",
            LicenseText.Reflow(TEXT));
    }

    [Fact]
    public void List_items_start_their_own_line()
    {
        const string TEXT = "Redistribution is permitted provided that:\n1. Redistributions of source code must retain\n   the above copyright notice.\n2. Redistributions in binary form\n   must reproduce it.\n- a dash item\n(a) a lettered item";

        Assert.Equal("Redistribution is permitted provided that:\n1. Redistributions of source code must retain the above copyright notice.\n2. Redistributions in binary form must reproduce it.\n- a dash item\n(a) a lettered item",
            LicenseText.Reflow(TEXT));
    }

    [Fact]
    public void Indented_sections_keep_their_indent_and_a_line_indented_less_starts_anew()
    {
        const string TEXT = "  0. Definitions.\n\n  \"This License\" refers to version 3\n  of the GNU General Public License.\nEND OF TERMS";

        Assert.Equal("  0. Definitions.\n\n  \"This License\" refers to version 3 of the GNU General Public License.\nEND OF TERMS",
            LicenseText.Reflow(TEXT));
    }

    [Fact]
    public void Windows_line_ends_are_read_like_others()
    {
        Assert.Equal("one two\n\nthree", LicenseText.Reflow("one\r\ntwo\r\n\r\nthree\r\n"));
    }
}
