namespace LogMark.Mcp.Prompts;

/// <summary>
/// Shared thinker lookup data used by study-thinker prompt and import tools.
/// Contains the canonical registry of known thinkers with their sources and metadata.
/// </summary>
public static class ThinkerRegistry
{
    public record ThinkerSource(string Type, string Url);

    public record ThinkerInfo(string Id, string Name, string Summary, List<ThinkerSource> Sources, string? InkSpace = null);

    /// <summary>
    /// Case-insensitive lookup of a thinker by name or alias.
    /// Returns null if not found.
    /// </summary>
    public static ThinkerInfo? Lookup(string query)
    {
        var key = query.Trim();
        return Thinkers.TryGetValue(key, out var info) ? info : null;
    }

    /// <summary>
    /// Returns true if the input starts with http:// or https:// (case-insensitive).
    /// </summary>
    public static bool IsUrl(string input)
    {
        return input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// For URLs, extracts the hostname (dots to hyphens, strips www-).
    /// For names, lowercases and trims.
    /// </summary>
    public static string SanitizeId(string input)
    {
        if (IsUrl(input))
        {
            try
            {
                var uri = new Uri(input);
                return uri.Host.Replace(".", "-").Replace("www-", "");
            }
            catch
            {
                return "direct-source";
            }
        }

        return input.ToLowerInvariant().Trim();
    }

    public static readonly Dictionary<string, ThinkerInfo> Thinkers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["karpathy"] = new ThinkerInfo(
            "karpathy", "Andrej Karpathy",
            "AI researcher bridging deep technical content with accessible education. Blog posts and X threads demonstrate the idea-to-artifact pipeline.",
            new List<ThinkerSource>
            {
                new("blog", "https://karpathy.github.io"),
                new("twitter", "https://x.com/karpathy")
            }),

        ["sivers"] = new ThinkerInfo(
            "sivers", "Derek Sivers",
            "20+ years of atomic, tightly edited essays on entrepreneurial and philosophical thinking. 330+ book notes.",
            new List<ThinkerSource>
            {
                new("blog", "https://sive.rs/blog"),
                new("book-notes", "https://sive.rs/book")
            }),

        ["willison"] = new ThinkerInfo(
            "willison", "Simon Willison",
            "Prolific builder-blogger documenting every tool, technique, and LLM experiment in public.",
            new List<ThinkerSource>
            {
                new("blog", "https://simonwillison.net"),
                new("twitter", "https://x.com/simonw")
            }),

        ["simonw"] = new ThinkerInfo(
            "willison", "Simon Willison",
            "Prolific builder-blogger documenting every tool, technique, and LLM experiment in public.",
            new List<ThinkerSource>
            {
                new("blog", "https://simonwillison.net"),
                new("twitter", "https://x.com/simonw")
            }),

        ["matuschak"] = new ThinkerInfo(
            "matuschak", "Andy Matuschak",
            "Inventor of the evergreen notes framework. Public working notes are the gold standard for linked, accumulative knowledge work.",
            new List<ThinkerSource>
            {
                new("notes", "https://notes.andymatuschak.org"),
                new("blog", "https://andymatuschak.org"),
                new("twitter", "https://x.com/andy_matuschak")
            }),

        ["nuckols"] = new ThinkerInfo(
            "nuckols", "Greg Nuckols",
            "Gold standard for evidence-based strength training writing. 5,000-15,000 word deep-dives analyzing meta-analyses.",
            new List<ThinkerSource>
            {
                new("blog", "https://www.strongerbyscience.com/articles")
            }),

        ["swyx"] = new ThinkerInfo(
            "swyx", "Shawn Wang (swyx)",
            "Coined 'AI Engineer' as a role. Latent Space podcast + newsletter covering AI tooling.",
            new List<ThinkerSource>
            {
                new("blog", "https://www.swyx.io"),
                new("newsletter", "https://latent.space"),
                new("twitter", "https://x.com/swyx")
            }),

        ["rauchg"] = new ThinkerInfo(
            "rauchg", "Guillermo Rauch",
            "Vercel/Next.js creator. Writing on developer experience, shipping fast, and AI + web development.",
            new List<ThinkerSource>
            {
                new("blog", "https://rauchg.com"),
                new("twitter", "https://x.com/rauchg")
            }),

        ["jvns"] = new ThinkerInfo(
            "jvns", "Julia Evans",
            "Makes systems programming accessible through blog posts and zines. Networking, debugging, git internals.",
            new List<ThinkerSource>
            {
                new("blog", "https://jvns.ca"),
                new("twitter", "https://x.com/b0rk")
            }),

        ["evans"] = new ThinkerInfo(
            "jvns", "Julia Evans",
            "Makes systems programming accessible through blog posts and zines. Networking, debugging, git internals.",
            new List<ThinkerSource>
            {
                new("blog", "https://jvns.ca"),
                new("twitter", "https://x.com/b0rk")
            }),

        ["appleton"] = new ThinkerInfo(
            "appleton", "Maggie Appleton",
            "Designer and writer who coined much of the digital garden visual language. Now at GitHub Next.",
            new List<ThinkerSource>
            {
                new("garden", "https://maggieappleton.com/garden"),
                new("twitter", "https://x.com/Mappletons")
            }),

        ["forte"] = new ThinkerInfo(
            "forte", "Tiago Forte",
            "Created Building a Second Brain (BASB/PARA/CODE). Defines the mainstream PKM movement.",
            new List<ThinkerSource>
            {
                new("blog", "https://fortelabs.com/blog"),
                new("twitter", "https://x.com/fortelabs")
            }),

        ["gwern"] = new ThinkerInfo(
            "gwern", "Gwern Branwen",
            "Independent researcher producing rigorously documented, continuously updated essays on AI, psychology, statistics.",
            new List<ThinkerSource>
            {
                new("essays", "https://gwern.net")
            }),

        ["scottyoung"] = new ThinkerInfo(
            "scottyoung", "Scott H. Young",
            "Documents aggressive self-directed learning experiments. 20-year archive of learning methodology.",
            new List<ThinkerSource>
            {
                new("blog", "https://www.scotthyoung.com/blog")
            }),

        ["kahl"] = new ThinkerInfo(
            "kahl", "Arvid Kahl",
            "Documents entire bootstrapped SaaS lifecycle with operational transparency -- decisions on pricing, feature cuts, marketing pivots.",
            new List<ThinkerSource>
            {
                new("blog", "https://thebootstrappedfounder.com"),
                new("twitter", "https://x.com/arvidkahl")
            }),

        ["chimero"] = new ThinkerInfo(
            "chimero", "Frank Chimero",
            "Philosophical essays on design process. Archive from 2009 covers design methodology, creativity under constraint.",
            new List<ThinkerSource>
            {
                new("blog", "https://frankchimero.com")
            }),

        ["zhuo"] = new ThinkerInfo(
            "zhuo", "Julie Zhuo",
            "Former VP Product Design at Facebook. Essays on craft of building products, design levels, trustworthy design.",
            new List<ThinkerSource>
            {
                new("newsletter", "https://lg.substack.com"),
                new("blog", "https://www.juliezhuo.com")
            }),

        ["perelman"] = new ThinkerInfo(
            "perelman", "Deb Perelman",
            "Documents recipe development process -- failures, iterations, years of testing. 20 years of process-documenting food writing.",
            new List<ThinkerSource>
            {
                new("blog", "https://smittenkitchen.com")
            }),

        ["craigmod"] = new ThinkerInfo(
            "craigmod", "Craig Mod",
            "Writer-photographer in Japan. Deep writing about technology, publishing, walking, photography.",
            new List<ThinkerSource>
            {
                new("essays", "https://craigmod.com"),
                new("twitter", "https://x.com/craigmod")
            }),

        ["dodds"] = new ThinkerInfo(
            "dodds", "Kent C. Dodds",
            "Developer educator (Epic Web, Testing Library). Prolific blogger on testing, React patterns, developer learning.",
            new List<ThinkerSource>
            {
                new("blog", "https://kentcdodds.com/blog"),
                new("twitter", "https://x.com/kentcdodds")
            }),

        ["ball"] = new ThinkerInfo(
            "ball", "Thorsten Ball",
            "Author of Writing an Interpreter/Compiler in Go. Explores craft of programming, dev tools, recreational coding.",
            new List<ThinkerSource>
            {
                new("blog", "https://thorstenball.com/blog"),
                new("newsletter", "https://registerspill.thorstenball.com")
            }),

        // --- Polymath family ---
        ["graham"] = new ThinkerInfo(
            "graham", "Paul Graham",
            "Programming (Viaweb/Yahoo Store), venture capital (Y Combinator), painting (RISD + Florence), essay writing (200+ essays). 'Hackers & Painters' as lived experience -- painting informed programming aesthetics.",
            new List<ThinkerSource>
            {
                new("essays", "https://paulgraham.com/articles.html"),
                new("twitter", "https://x.com/paulg")
            }),
        ["paulgraham"] = new ThinkerInfo(
            "graham", "Paul Graham",
            "Programming (Viaweb/Yahoo Store), venture capital (Y Combinator), painting (RISD + Florence), essay writing (200+ essays). 'Hackers & Painters' as lived experience -- painting informed programming aesthetics.",
            new List<ThinkerSource>
            {
                new("essays", "https://paulgraham.com/articles.html"),
                new("twitter", "https://x.com/paulg")
            }),
        ["cowen"] = new ThinkerInfo(
            "cowen", "Tyler Cowen",
            "Economics professor, cultural commentator (food, music, art -- genuine depth), daily blogger since 2003 (8000+ posts). Called 'the Prototypic Polymath' by Eric Topol.",
            new List<ThinkerSource>
            {
                new("blog", "https://marginalrevolution.com"),
                new("podcast", "https://conversationswithtyler.com"),
                new("twitter", "https://x.com/tylercowen")
            }),
        ["tylercowen"] = new ThinkerInfo(
            "cowen", "Tyler Cowen",
            "Economics professor, cultural commentator (food, music, art -- genuine depth), daily blogger since 2003 (8000+ posts). Called 'the Prototypic Polymath' by Eric Topol.",
            new List<ThinkerSource>
            {
                new("blog", "https://marginalrevolution.com"),
                new("podcast", "https://conversationswithtyler.com"),
                new("twitter", "https://x.com/tylercowen")
            }),
        ["linuslee"] = new ThinkerInfo(
            "linuslee", "Linus Lee",
            "Builds programming languages (Oak), AI tools, knowledge tools (Monocle), UI experiments. 500K+ words on blog, 100+ side projects spanning compilers to creative tools.",
            new List<ThinkerSource>
            {
                new("blog", "https://thesephist.com"),
                new("github", "https://github.com/thesephist")
            }),
        ["thesephist"] = new ThinkerInfo(
            "linuslee", "Linus Lee",
            "Builds programming languages (Oak), AI tools, knowledge tools (Monocle), UI experiments. 500K+ words on blog, 100+ side projects spanning compilers to creative tools.",
            new List<ThinkerSource>
            {
                new("blog", "https://thesephist.com"),
                new("github", "https://github.com/thesephist")
            }),
        ["dhh"] = new ThinkerInfo(
            "dhh", "David Heinemeier Hansson",
            "Software (Ruby on Rails, Basecamp, HEY), motorsport (Le Mans class winner 2014), writing (5 books), photography.",
            new List<ThinkerSource>
            {
                new("blog", "https://world.hey.com/dhh"),
                new("blog2", "https://dhh.dk"),
                new("twitter", "https://x.com/dhh")
            }),
        ["ciechanowski"] = new ThinkerInfo(
            "ciechanowski", "Bartosz Ciechanowski",
            "Interactive essays bridging engineering, physics, mathematics, and web visualization. Each essay requires deep understanding of BOTH the topic AND interaction design.",
            new List<ThinkerSource>
            {
                new("essays", "https://ciechanow.ski")
            }),
        ["victor"] = new ThinkerInfo(
            "victor", "Bret Victor",
            "Electrical engineering (Caltech), synthesizer design (Alesis), Apple product design (iPad, Apple Watch), spatial computing (Dynamicland/Realtalk). 'Inventing on Principle' is a polymath manifesto.",
            new List<ThinkerSource>
            {
                new("essays", "https://worrydream.com")
            }),
        ["bretvictor"] = new ThinkerInfo(
            "victor", "Bret Victor",
            "Electrical engineering (Caltech), synthesizer design (Alesis), Apple product design (iPad, Apple Watch), spatial computing (Dynamicland/Realtalk). 'Inventing on Principle' is a polymath manifesto.",
            new List<ThinkerSource>
            {
                new("essays", "https://worrydream.com")
            }),
        ["karlsson"] = new ThinkerInfo(
            "karlsson", "Henrik Karlsson",
            "Former programmer, factory worker, poet, teacher, lab technician. Each Escaping Flatland essay is genuine cross-domain synthesis. 47K+ subscribers.",
            new List<ThinkerSource>
            {
                new("newsletter", "https://henrikkarlsson.xyz")
            }),
        ["henrikkarlsson"] = new ThinkerInfo(
            "karlsson", "Henrik Karlsson",
            "Former programmer, factory worker, poet, teacher, lab technician. Each Escaping Flatland essay is genuine cross-domain synthesis. 47K+ subscribers.",
            new List<ThinkerSource>
            {
                new("newsletter", "https://henrikkarlsson.xyz")
            }),
        ["webb"] = new ThinkerInfo(
            "webb", "Matt Webb",
            "Physics (Oxford MPhys), product design (BERG, work in MoMA), hardware invention (Poem/1). 26 years of blog posts on technology, design, and society.",
            new List<ThinkerSource>
            {
                new("blog", "https://interconnected.org"),
                new("twitter", "https://x.com/genmon")
            }),
        ["mattwebb"] = new ThinkerInfo(
            "webb", "Matt Webb",
            "Physics (Oxford MPhys), product design (BERG, work in MoMA), hardware invention (Poem/1). 26 years of blog posts on technology, design, and society.",
            new List<ThinkerSource>
            {
                new("blog", "https://interconnected.org"),
                new("twitter", "https://x.com/genmon")
            }),
        // --- Frontier polymaths ---
        ["taleb"] = new ThinkerInfo(
            "taleb", "Nassim Nicholas Taleb",
            "Probability, risk, fat tails (Incerto series), former options trader. Polymath across classical languages (Greek, Aramaic, Arabic), Mediterranean culture and cuisine, Orthodox Christianity, Levantine history, fitness (deadlifts as antifragility), Stoic philosophy, formal mathematics. His framework (antifragility, skin in the game) is itself a cross-domain theory.",
            new List<ThinkerSource>
            {
                new("twitter", "https://x.com/nntaleb"),
                new("blog", "https://medium.com/@nntaleb"),
                new("academic", "https://fooledbyrandomness.com")
            }),
        ["nntaleb"] = new ThinkerInfo(
            "taleb", "Nassim Nicholas Taleb",
            "Probability, risk, fat tails (Incerto series), former options trader. Polymath across classical languages (Greek, Aramaic, Arabic), Mediterranean culture and cuisine, Orthodox Christianity, Levantine history, fitness (deadlifts as antifragility), Stoic philosophy, formal mathematics. His framework (antifragility, skin in the game) is itself a cross-domain theory.",
            new List<ThinkerSource>
            {
                new("twitter", "https://x.com/nntaleb"),
                new("blog", "https://medium.com/@nntaleb"),
                new("academic", "https://fooledbyrandomness.com")
            })
    };
}
