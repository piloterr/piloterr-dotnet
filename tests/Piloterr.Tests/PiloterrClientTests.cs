using System.Net;
using System.Text;
using System.Text.Json;
using Piloterr;
using Xunit;

namespace Piloterr.Tests;

public sealed class PiloterrClientTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = new();

        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = _ => Json(200, "{\"ok\":true}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return Responder(request);
        }
    }

    private static HttpResponseMessage Json(int status, string body) => new((HttpStatusCode)status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private readonly RecordingHandler _handler = new();
    private readonly PiloterrClient _client;

    public PiloterrClientTests()
    {
        _client = new PiloterrClient(new PiloterrClientOptions
        {
            ApiKey = "test-key",
            HttpClient = new HttpClient(_handler),
            RetryDelay = TimeSpan.FromMilliseconds(1),
        });
    }

    private PiloterrClient ClientWithBaseUrl(string baseUrl) =>
        new(new PiloterrClientOptions { ApiKey = "k", BaseUrl = baseUrl, HttpClient = new HttpClient(_handler) });

    [Fact]
    public void RequiresApiKey()
    {
        Environment.SetEnvironmentVariable("PILOTERR_API_KEY", null);
        Assert.Throws<PiloterrConfigurationException>(() => new PiloterrClient());
        Assert.Throws<PiloterrConfigurationException>(() => new PiloterrClient(new PiloterrClientOptions { ApiKey = "   " }));
        Assert.Throws<PiloterrConfigurationException>(() => new PiloterrClient(new PiloterrClientOptions { ApiKey = "k", BaseUrl = "ftp://x" }));
    }

    [Fact]
    public async Task FeaturedOperation()
    {
        _handler.Responder = _ => Json(200, "{\"hello\":\"world\"}");
        var response = await _client.AmazonProductAsync(new AmazonProductParams { Query = "B09DFCB66S" });
        Assert.Equal("world", response.Json.GetProperty("hello").GetString());
        var request = _handler.Requests[0].Request;
        Assert.Equal("GET", request.Method.Method);
        Assert.Equal("https://api.piloterr.com/v2/amazon/product", request.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Equal("test-key", request.Headers.GetValues("x-api-key").Single());
        Assert.StartsWith("piloterr-dotnet/", string.Join(" ", request.Headers.GetValues("User-Agent")));
    }

    [Fact]
    public async Task QuerySerialization()
    {
        using var client = ClientWithBaseUrl("https://example.test/");
        await client.RequestAsync("GET", "/v1/anything", new Dictionary<string, object?>
        {
            ["text"] = "a b&c",
            ["flag"] = false,
            ["count"] = 0,
            ["ratio"] = 1.5,
            ["whole"] = 2.0,
            ["list"] = new[] { "x", "y" },
            ["nothing"] = null,
        });
        Assert.Equal(
            "https://example.test/v1/anything?text=a%20b%26c&flag=false&count=0&ratio=1.5&whole=2&list=x&list=y",
            _handler.Requests[0].Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task PostSendsJsonBody()
    {
        await _client.RequestAsync("POST", "/v1/anything", new Dictionary<string, object?> { ["query"] = "x", ["page"] = 2, ["gone"] = null });
        var (request, body) = _handler.Requests[0];
        Assert.Equal("POST", request.Method.Method);
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("{\"query\":\"x\",\"page\":2}", body);
        Assert.Equal(string.Empty, request.RequestUri!.Query);
    }

    [Fact]
    public async Task PostKeepsGlobalParametersInQuery()
    {
        await _client.RequestAsync("POST", "/v1/anything", new Dictionary<string, object?> { ["query"] = "x", ["is_async"] = true, ["fields"] = "a" });
        var (request, body) = _handler.Requests[0];
        Assert.Equal("{\"query\":\"x\"}", body);
        Assert.Equal("?is_async=true&fields=a", request.RequestUri!.Query);
    }

    [Fact]
    public async Task PathParameters()
    {
        await _client.RequestAsync("GET", "/v1/items/{id}", new Dictionary<string, object?> { ["id"] = "a/b c" });
        Assert.Equal("https://api.piloterr.com/v1/items/a%2Fb%20c", _handler.Requests[0].Request.RequestUri!.AbsoluteUri);
        await Assert.ThrowsAsync<PiloterrSerializationException>(() => _client.RequestAsync("GET", "/v1/items/{id}"));
    }

    [Fact]
    public async Task UnsupportedMethod()
    {
        await Assert.ThrowsAsync<PiloterrConfigurationException>(() => _client.JobsAsync(new JobsParams { Id = "test" }, new RequestOptions { Method = "POST" }));
    }

    [Fact]
    public async Task DualOperationCanUsePost()
    {
        await _client.AboutyouProductAsync(new AboutyouProductParams { Query = "test" }, new RequestOptions { Method = "POST" });
        var (request, body) = _handler.Requests[0];
        Assert.Equal("POST", request.Method.Method);
        Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(body!).RootElement.ValueKind);
    }

    [Fact]
    public async Task EveryEndpoint()
    {
        var calls = new Dictionary<string, Func<Task<ApiResponse>>>
        {
            ["aboutyou_product"] = () => _client.AboutyouProductAsync(new AboutyouProductParams { Query = "test" }),
            ["aboutyou_search"] = () => _client.AboutyouSearchAsync(new AboutyouSearchParams { Query = "suede bloom" }),
            ["action_product"] = () => _client.ActionProductAsync(new ActionProductParams { Query = "3216159" }),
            ["action_search"] = () => _client.ActionSearchAsync(new ActionSearchParams { Query = "meuble" }),
            ["aldi_product"] = () => _client.AldiProductAsync(new AldiProductParams { Query = "fiches-produits/filets-danchois-marines-6806.html" }),
            ["aldi_search"] = () => _client.AldiSearchAsync(new AldiSearchParams { Query = "produits/viande-poisson/poisson-crustace.html" }),
            ["alibaba_product"] = () => _client.AlibabaProductAsync(new AlibabaProductParams { Query = "https://www.alibaba.com/product-detail/Custom-Neon-Light-Acrylic-Neon-Sign_1601175379813.html" }),
            ["alibaba_search"] = () => _client.AlibabaSearchAsync(new AlibabaSearchParams { Query = "https://www.alibaba.com/trade/search?SearchText=neon" }),
            ["aliexpress_product"] = () => _client.AliexpressProductAsync(new AliexpressProductParams { Query = "https://www.aliexpress.com/item/1005009517624640.html" }),
            ["aliexpress_product_reviews"] = () => _client.AliexpressProductReviewsAsync(new AliexpressProductReviewsParams { Query = "1005001388303149" }),
            ["aliexpress_search"] = () => _client.AliexpressSearchAsync(new AliexpressSearchParams { Query = "meta" }),
            ["aliexpress_store_product"] = () => _client.AliexpressStoreProductAsync(new AliexpressStoreProductParams { Query = "https://www.aliexpress.com/store/1101705614" }),
            ["allegro_product"] = () => _client.AllegroProductAsync(new AllegroProductParams { Query = "https://allegro.pl/produkt/slug-uuid?offerId=123" }),
            ["allegro_search"] = () => _client.AllegroSearchAsync(new AllegroSearchParams { Query = "mufowa" }),
            ["amazon_product"] = () => _client.AmazonProductAsync(new AmazonProductParams { Query = "B09DFCB66S" }),
            ["amazon_product_offer"] = () => _client.AmazonProductOfferAsync(new AmazonProductOfferParams { Query = "B093C9B1HK" }),
            ["amazon_search"] = () => _client.AmazonSearchAsync(new AmazonSearchParams { Query = "playstation 5" }),
            ["apartments_property"] = () => _client.ApartmentsPropertyAsync(new ApartmentsPropertyParams { Query = "https://www.apartments.com/141-e-33rd-st-new-york-ny-unit-si-fl16-id43/p6b4mes/" }),
            ["apartments_search"] = () => _client.ApartmentsSearchAsync(new ApartmentsSearchParams { Query = "https://www.apartments.com/new-york-ny/3" }),
            ["argenprop_property"] = () => _client.ArgenpropPropertyAsync(new ArgenpropPropertyParams { Query = "https://www.argenprop.com/departamento-en-venta-en-palermo-chico-5-ambientes--20724993" }),
            ["argenprop_search"] = () => _client.ArgenpropSearchAsync(new ArgenpropSearchParams { Query = "palermo" }),
            ["argos_product"] = () => _client.ArgosProductAsync(new ArgosProductParams { Query = "test" }),
            ["argos_search"] = () => _client.ArgosSearchAsync(new ArgosSearchParams { Query = "laptop" }),
            ["arxiv_paper"] = () => _client.ArxivPaperAsync(new ArxivPaperParams { Query = "2201.00978 or https://arxiv.org/abs/cond-mat/0011267v1." }),
            ["arxiv_search"] = () => _client.ArxivSearchAsync(new ArxivSearchParams { Query = "electron. A paper URL such as https://arxiv.org/abs/2201.00978 returns 400." }),
            ["auchan_product"] = () => _client.AuchanProductAsync(new AuchanProductParams { Query = "https://www.auchan.fr/charles-alice-specialite-pommes-mangues-sans-sucres-ajoutes/pr-C1177497" }),
            ["auchan_search"] = () => _client.AuchanSearchAsync(new AuchanSearchParams { Query = "pomme" }),
            ["autodoc_product"] = () => _client.AutodocProductAsync(new AutodocProductParams { Query = "https://www.auto-doc.fr/ridex/7999541" }),
            ["autodoc_product_related"] = () => _client.AutodocProductRelatedAsync(new AutodocProductRelatedParams { Query = "https://www.auto-doc.fr/ridex/7999541", MakerId = 93, VehicleType = "pkw", MakerName = "RENAULT", ProductId = 7999541 }),
            ["autodoc_search"] = () => _client.AutodocSearchAsync(new AutodocSearchParams { Query = "https://www.auto-doc.fr/search?keyword=frein" }),
            ["autoeurope_result"] = () => _client.AutoeuropeResultAsync(new AutoeuropeResultParams { OfferId = "3CD30DE2D9BE4CE8FC28E20D2702FCC7", Pickup = "CDG", PickupDate = "2026-11-06", DropoffDate = "2026-11-09" }),
            ["autoeurope_search"] = () => _client.AutoeuropeSearchAsync(new AutoeuropeSearchParams { Pickup = "CDG", PickupDate = "2026-11-06", DropoffDate = "2026-11-09" }),
            ["autoscout24_ad"] = () => _client.Autoscout24AdAsync(new Autoscout24AdParams { Query = "https://www.autoscout24.fr/offres/porsche-macan-s-diesel-pano-1-hand-diesel-or-cat_ma57mo20311-547632b2-e4fc-47f3-8143-1a2d7b276366" }),
            ["autoscout24_search"] = () => _client.Autoscout24SearchAsync(new Autoscout24SearchParams { Query = "https://www.autoscout24.fr/lst/volkswagen/golf?atype=C" }),
            ["autotrader_ad"] = () => _client.AutotraderAdAsync(new AutotraderAdParams { Query = "https://www.autotrader.co.uk/car-details/202606053030491" }),
            ["autotrader_search"] = () => _client.AutotraderSearchAsync(new AutotraderSearchParams { Query = "https://www.autotrader.co.uk/car-search?channel=cars&postcode=W1F%208EB&price_search_type=total&sort=relevance&page=1" }),
            ["bestbuy_product"] = () => _client.BestbuyProductAsync(new BestbuyProductParams { Query = "https://www.bestbuy.com/product/gigabyte-aero-x16-copilot-pc-165hz-wqxga-rtx-5070-ryzen-ai-7-350-1tb-ssd-16gb-ddr5-windows-11-lunar-white/J3GWPQC2RQ" }),
            ["bestbuy_product_reviews"] = () => _client.BestbuyProductReviewsAsync(new BestbuyProductReviewsParams { Query = "test" }),
            ["bestbuy_search"] = () => _client.BestbuySearchAsync(new BestbuySearchParams { Query = "machine" }),
            ["bhphotovideo_product"] = () => _client.BhphotovideoProductAsync(new BhphotovideoProductParams { Query = "1948568-REG" }),
            ["bhphotovideo_search"] = () => _client.BhphotovideoSearchAsync(new BhphotovideoSearchParams { Query = "sandisk extreme pro" }),
            ["bing_search"] = () => _client.BingSearchAsync(new BingSearchParams { Query = "openai" }),
            ["bitiba_product"] = () => _client.BitibaProductAsync(new BitibaProductParams { Query = "75036. A search link returns 400." }),
            ["bitiba_search"] = () => _client.BitibaSearchAsync(new BitibaSearchParams { Query = "test" }),
            ["bol_product"] = () => _client.BolProductAsync(new BolProductParams { Query = "9300000240171676" }),
            ["bol_search"] = () => _client.BolSearchAsync(new BolSearchParams { Query = "lego" }),
            ["boulanger_product"] = () => _client.BoulangerProductAsync(new BoulangerProductParams { Query = "https://www.boulanger.com/ref/1193303" }),
            ["boulanger_search"] = () => _client.BoulangerSearchAsync(new BoulangerSearchParams { Query = "aspirateur" }),
            ["brave_search"] = () => _client.BraveSearchAsync(new BraveSearchParams { Query = "anthropic" }),
            ["bricodepot_product"] = () => _client.BricodepotProductAsync(new BricodepotProductParams { Query = "https://www.bricodepot.fr/p/3373270014082/compresseur-coaxial-50l-vertical-10-bars-accessoires-1" }),
            ["bricodepot_search"] = () => _client.BricodepotSearchAsync(new BricodepotSearchParams { Query = "https://www.bricodepot.fr/produits/outillage/outillage-electroportatif-a-main-et-accessoire/perceuse-visseuse-et-outillage-de-percage/perceuse-visseuse-et-perceuse-a-percussion-sans-fil" }),
            ["bricorama_product"] = () => _client.BricoramaProductAsync(new BricoramaProductParams { Query = "https://www.bricorama.fr/p/perceuse-visseuse-sans-fil-18v/3999991691267" }),
            ["bricorama_search"] = () => _client.BricoramaSearchAsync(new BricoramaSearchParams { Query = "perceuse" }),
            ["carrefour_product"] = () => _client.CarrefourProductAsync(new CarrefourProductParams { Query = "https://www.carrefour.fr/p/oeufs-de-caille-le-gaulois-3266980075934" }),
            ["carrefour_search"] = () => _client.CarrefourSearchAsync(new CarrefourSearchParams { Query = "pomme" }),
            ["carrefour_suggest"] = () => _client.CarrefourSuggestAsync(new CarrefourSuggestParams { Query = "coca" }),
            ["castorama_product"] = () => _client.CastoramaProductAsync(new CastoramaProductParams { Query = "https://www.castorama.fr/mastic-bain-et-cuisine-rubson-blanc-280ml/5410091628840_CAFR.prd" }),
            ["castorama_search"] = () => _client.CastoramaSearchAsync(new CastoramaSearchParams { Query = "joint-silicone" }),
            ["cdiscount_product"] = () => _client.CdiscountProductAsync(new CdiscountProductParams { Query = "https://www.cdiscount.com/informatique/ordinateurs-pc-portables/acemagic-pc-portables-gaming/f-1070992-aaaph06865.html" }),
            ["cdiscount_product_offer"] = () => _client.CdiscountProductOfferAsync(new CdiscountProductOfferParams { Query = "https://www.cdiscount.com/mp-352880-auc3283981039048.html" }),
            ["cdiscount_search"] = () => _client.CdiscountSearchAsync(new CdiscountSearchParams { Query = "https://www.cdiscount.com/search/10/laptop.html" }),
            ["centris_property"] = () => _client.CentrisPropertyAsync(new CentrisPropertyParams { Query = "https://www.centris.ca/en/condos~for-sale~montreal-le-sud-ouest/17796540" }),
            ["centris_search"] = () => _client.CentrisSearchAsync(new CentrisSearchParams { Query = "montreal" }),
            ["chewy_product"] = () => _client.ChewyProductAsync(new ChewyProductParams { Query = "test" }),
            ["chewy_search"] = () => _client.ChewySearchAsync(new ChewySearchParams { Query = "test" }),
            ["clearedjobs_job"] = () => _client.ClearedjobsJobAsync(new ClearedjobsJobParams { Query = "analyst-washington-district-of-columbia-1902823." }),
            ["clearedjobs_search"] = () => _client.ClearedjobsSearchAsync(new ClearedjobsSearchParams { Query = "analyst. A job URL returns 400." }),
            ["company"] = () => _client.CompanyAsync(new CompanyParams { Query = "gucci.com" }),
            ["coolblue_product"] = () => _client.CoolblueProductAsync(new CoolblueProductParams { Query = "https://www.coolblue.be/nl/product/905648/sony-wh-1000xm5-zwart.html" }),
            ["coolblue_search"] = () => _client.CoolblueSearchAsync(new CoolblueSearchParams { Query = "sony wh-1000xm5" }),
            ["copart_lot"] = () => _client.CopartLotAsync(new CopartLotParams { Query = "55295666" }),
            ["copart_search"] = () => _client.CopartSearchAsync(new CopartSearchParams { Query = "chevrolet" }),
            ["costco_product"] = () => _client.CostcoProductAsync(new CostcoProductParams { Query = "4000266385" }),
            ["costco_search"] = () => _client.CostcoSearchAsync(new CostcoSearchParams { Query = "home" }),
            ["crunchbase_company_info"] = () => _client.CrunchbaseCompanyInfoAsync(new CrunchbaseCompanyInfoParams()),
            ["crunchbase_event"] = () => _client.CrunchbaseEventAsync(new CrunchbaseEventParams { Query = "london-tech-week-2025" }),
            ["crunchbase_funding_round"] = () => _client.CrunchbaseFundingRoundAsync(new CrunchbaseFundingRoundParams { Query = "openai-secondary-market--3bab6e98" }),
            ["crunchbase_funding_rounds"] = () => _client.CrunchbaseFundingRoundsAsync(new CrunchbaseFundingRoundsParams()),
            ["crunchbase_people_info"] = () => _client.CrunchbasePeopleInfoAsync(new CrunchbasePeopleInfoParams { Query = "alex-martin" }),
            ["crunchbase_search"] = () => _client.CrunchbaseSearchAsync(new CrunchbaseSearchParams { Query = "openai" }),
            ["cultura_product"] = () => _client.CulturaProductAsync(new CulturaProductParams { Query = "https://www.cultura.com/p-harry-potter-et-le-prisonnier-d-azkaban-illustre-par-minalima-9782075193993.html" }),
            ["cultura_search"] = () => _client.CulturaSearchAsync(new CulturaSearchParams { Query = "harry potter" }),
            ["darty_product"] = () => _client.DartyProductAsync(new DartyProductParams { Query = "https://www.darty.com/nav/achat/petit_electromenager/aspirateur-balai_main/aspirateur_balai/dyson_piston_animal.html" }),
            ["darty_search"] = () => _client.DartySearchAsync(new DartySearchParams { Query = "aspirateur" }),
            ["datasets_linkedin_company"] = () => _client.DatasetsLinkedinCompanyAsync(new DatasetsLinkedinCompanyParams()),
            ["decathlon_product"] = () => _client.DecathlonProductAsync(new DecathlonProductParams { Query = "https://www.decathlon.fr/p/gourde-alu-1l-avec-bouchon-instantane-composee-a-100percent-dalu-recycle-bleu/330041/c98m8612536" }),
            ["decathlon_search"] = () => _client.DecathlonSearchAsync(new DecathlonSearchParams { Query = "gourde" }),
            ["digikey_product"] = () => _client.DigikeyProductAsync(new DigikeyProductParams { Query = "https://www.digikey.fr/fr/products/detail/phoenix-contact/2981965/3604293" }),
            ["digikey_search"] = () => _client.DigikeySearchAsync(new DigikeySearchParams { Query = "phoenix contact" }),
            ["doaj_article"] = () => _client.DoajArticleAsync(new DoajArticleParams { Query = "000004687a3a411cb466625281f2ceb0." }),
            ["doaj_journal"] = () => _client.DoajJournalAsync(new DoajJournalParams { Query = "2747-173X." }),
            ["doaj_search"] = () => _client.DoajSearchAsync(new DoajSearchParams { Query = "climate. An article or journal URL returns 400." }),
            ["domain_dnsbl"] = () => _client.DomainDnsblAsync(new DomainDnsblParams { Query = "185.122.10.10" }),
            ["domain_malicious"] = () => _client.DomainMaliciousAsync(new DomainMaliciousParams { Query = "197.33.189.70" }),
            ["domain_whois"] = () => _client.DomainWhoisAsync(new DomainWhoisParams { Query = "piloterr.com" }),
            ["domaincomau_property"] = () => _client.DomaincomauPropertyAsync(new DomaincomauPropertyParams { Query = "https://www.domain.com.au/7d-27-31-ocean-street-n-bondi-nsw-2026-2021178032" }),
            ["domaincomau_search"] = () => _client.DomaincomauSearchAsync(new DomaincomauSearchParams { Query = "Bondi NSW 2026" }),
            ["duckduckgo_search"] = () => _client.DuckduckgoSearchAsync(new DuckduckgoSearchParams { Query = "test" }),
            ["ebay_product"] = () => _client.EbayProductAsync(new EbayProductParams { Query = "257758922911" }),
            ["ebay_search"] = () => _client.EbaySearchAsync(new EbaySearchParams { Query = "laptop" }),
            ["elcorteingles_product"] = () => _client.ElcorteinglesProductAsync(new ElcorteinglesProductParams { Query = "A200910175" }),
            ["elcorteingles_search"] = () => _client.ElcorteinglesSearchAsync(new ElcorteinglesSearchParams { Query = "marino" }),
            ["eleclerc_product"] = () => _client.EleclercProductAsync(new EleclercProductParams { Query = "3564700201442" }),
            ["eleclerc_search"] = () => _client.EleclercSearchAsync(new EleclercSearchParams { Query = "pomme" }),
            ["eleclerc_suggest"] = () => _client.EleclercSuggestAsync(new EleclercSuggestParams { Query = "pomme" }),
            ["email_analyzes"] = () => _client.EmailAnalyzesAsync(new EmailAnalyzesParams { Query = "airbus.com" }),
            ["email_finder"] = () => _client.EmailFinderAsync(new EmailFinderParams { Query = "Jane Doe" }),
            ["email_verify"] = () => _client.EmailVerifyAsync(new EmailVerifyParams { Query = "jane.doe@example.com" }),
            ["europages_company"] = () => _client.EuropagesCompanyAsync(new EuropagesCompanyParams { Query = "https://www.europages.fr/fr/company/synyco-sas-22106635" }),
            ["europages_search"] = () => _client.EuropagesSearchAsync(new EuropagesSearchParams { Query = "emballage" }),
            ["eventbrite_event"] = () => _client.EventbriteEventAsync(new EventbriteEventParams { Query = "2002625586037" }),
            ["eventbrite_search"] = () => _client.EventbriteSearchAsync(new EventbriteSearchParams()),
            ["expedia_ad"] = () => _client.ExpediaAdAsync(new ExpediaAdParams { Query = "https://www.expedia.com/New-York-Hotels-Hilton-New-York-Fashion-District.h3117215.Hotel-Information" }),
            ["expedia_flight"] = () => _client.ExpediaFlightAsync(new ExpediaFlightParams { Query = "JFK-LAX" }),
            ["expedia_search"] = () => _client.ExpediaSearchAsync(new ExpediaSearchParams { Query = "https://euro.expedia.net/Hotel-Search?currency=EUR&siteid=4400&destination=Toulouse" }),
            ["fashionphile_product"] = () => _client.FashionphileProductAsync(new FashionphileProductParams { Query = "celine-supple-shiny-lambskin-little-new-luggage-black-1978804" }),
            ["fashionphile_search"] = () => _client.FashionphileSearchAsync(new FashionphileSearchParams { Query = "celine" }),
            ["finder_chewy_products"] = () => _client.FinderChewyProductsAsync(new FinderChewyProductsParams()),
            ["finder_eleclerc_stores"] = () => _client.FinderEleclercStoresAsync(new FinderEleclercStoresParams { Query = "paris" }),
            ["finder_google_countries"] = () => _client.FinderGoogleCountriesAsync(new FinderGoogleCountriesParams { Query = "fr" }),
            ["finder_google_languages"] = () => _client.FinderGoogleLanguagesAsync(new FinderGoogleLanguagesParams { Query = "french" }),
            ["finder_google_locations"] = () => _client.FinderGoogleLocationsAsync(new FinderGoogleLocationsParams { Query = "Paris" }),
            ["finder_leroymerlin_products"] = () => _client.FinderLeroymerlinProductsAsync(new FinderLeroymerlinProductsParams()),
            ["finder_leroymerlin_stores"] = () => _client.FinderLeroymerlinStoresAsync(new FinderLeroymerlinStoresParams { Query = "balma" }),
            ["finder_linkedin_industries"] = () => _client.FinderLinkedinIndustriesAsync(new FinderLinkedinIndustriesParams { Query = "software development" }),
            ["finder_mrbricolage_stores"] = () => _client.FinderMrbricolageStoresAsync(new FinderMrbricolageStoresParams { Query = "matignon" }),
            ["fiverr_profile_info"] = () => _client.FiverrProfileInfoAsync(new FiverrProfileInfoParams { Query = "https://www.fiverr.com/username" }),
            ["fiverr_search"] = () => _client.FiverrSearchAsync(new FiverrSearchParams { Query = "https://www.fiverr.com/search/gigs?query=website+development" }),
            ["fnac_product"] = () => _client.FnacProductAsync(new FnacProductParams { Query = "23428278" }),
            ["fnac_search"] = () => _client.FnacSearchAsync(new FnacSearchParams { Query = "iphone" }),
            ["fotocasa_ad"] = () => _client.FotocasaAdAsync(new FotocasaAdParams { Query = "https://www.fotocasa.es/es/comprar/vivienda/madrid-capital/aire-acondicionado-calefaccion-terraza-ascensor-amueblado/190589697/d" }),
            ["fotocasa_search"] = () => _client.FotocasaSearchAsync(new FotocasaSearchParams { Query = "madrid" }),
            ["franchisedirect_franchise"] = () => _client.FranchisedirectFranchiseAsync(new FranchisedirectFranchiseParams { Query = "https://www.franchisedirect.com/coffee-franchises/coffeenow" }),
            ["franchisedirect_search"] = () => _client.FranchisedirectSearchAsync(new FranchisedirectSearchParams { Query = "coffee" }),
            ["fressnapf_product"] = () => _client.FressnapfProductAsync(new FressnapfProductParams { Query = "https://www.fressnapf.de/p/royal-canin-maxi-adult-15-kg-1003112004/" }),
            ["fressnapf_search"] = () => _client.FressnapfSearchAsync(new FressnapfSearchParams { Query = "halsband" }),
            ["funda_property"] = () => _client.FundaPropertyAsync(new FundaPropertyParams { Query = "https://www.funda.nl/detail/koop/amsterdam/appartement-hillegomstraat-57-1/44654106/" }),
            ["funda_search"] = () => _client.FundaSearchAsync(new FundaSearchParams { Query = "amsterdam" }),
            ["g2_product_info"] = () => _client.G2ProductInfoAsync(new G2ProductInfoParams { Query = "postman" }),
            ["github_user_info"] = () => _client.GithubUserInfoAsync(new GithubUserInfoParams { Query = "https://github.com/example-user" }),
            ["glassdoor_company_info"] = () => _client.GlassdoorCompanyInfoAsync(new GlassdoorCompanyInfoParams { Query = "1692882" }),
            ["glassdoor_company_search"] = () => _client.GlassdoorCompanySearchAsync(new GlassdoorCompanySearchParams { Query = "airbus" }),
            ["glassdoor_job_info"] = () => _client.GlassdoorJobInfoAsync(new GlassdoorJobInfoParams { Query = "1010283388161" }),
            ["glassdoor_job_search"] = () => _client.GlassdoorJobSearchAsync(new GlassdoorJobSearchParams { Query = "security" }),
            ["goodreads_book"] = () => _client.GoodreadsBookAsync(new GoodreadsBookParams { Query = "44767458" }),
            ["goodreads_search"] = () => _client.GoodreadsSearchAsync(new GoodreadsSearchParams { Query = "dune" }),
            ["google_images"] = () => _client.GoogleImagesAsync(new GoogleImagesParams { Query = "mountain landscape" }),
            ["google_jobs"] = () => _client.GoogleJobsAsync(new GoogleJobsParams { Query = "software engineer" }),
            ["google_maps"] = () => _client.GoogleMapsAsync(new GoogleMapsParams { Query = "coffee shop paris" }),
            ["google_news"] = () => _client.GoogleNewsAsync(new GoogleNewsParams { Query = "artificial intelligence news" }),
            ["google_search"] = () => _client.GoogleSearchAsync(new GoogleSearchParams { Query = "best electric cars 2024" }),
            ["google_search_ai"] = () => _client.GoogleSearchAiAsync(new GoogleSearchAiParams { Query = "what is mozilla firefox" }),
            ["google_search_autocomplete"] = () => _client.GoogleSearchAutocompleteAsync(new GoogleSearchAutocompleteParams { Query = "coffee" }),
            ["google_shopping"] = () => _client.GoogleShoppingAsync(new GoogleShoppingParams { Query = "playstation 5" }),
            ["google_videos"] = () => _client.GoogleVideosAsync(new GoogleVideosParams { Query = "paris travel guide" }),
            ["googleadstransparency_ad"] = () => _client.GoogleadstransparencyAdAsync(new GoogleadstransparencyAdParams { Query = "https://adstransparency.google.com/advertiser/AR10904289436120383489/creative/CR10927354991047868417?region=FR" }),
            ["googleadstransparency_search"] = () => _client.GoogleadstransparencySearchAsync(new GoogleadstransparencySearchParams { Query = "webapi.group" }),
            ["grainger_product"] = () => _client.GraingerProductAsync(new GraingerProductParams { Query = "3FRU1" }),
            ["grainger_search"] = () => _client.GraingerSearchAsync(new GraingerSearchParams { Query = "carolina" }),
            ["greenhouse_job"] = () => _client.GreenhouseJobAsync(new GreenhouseJobParams { Query = "discord/8806482002 or https://job-boards.greenhouse.io/discord/jobs/8806482002." }),
            ["greenhouse_search"] = () => _client.GreenhouseSearchAsync(new GreenhouseSearchParams { Query = "discord. A job URL returns 400." }),
            ["gsaelibrary_contract"] = () => _client.GsaelibraryContractAsync(new GsaelibraryContractParams { Query = "https://www.gsaelibrary.gsa.gov/ElibMain/contractorInfo.do?contractNumber=GS-07F-0239T&contractorName=US+RADAR+INC&executeQuery=YES" }),
            ["gsaelibrary_search"] = () => _client.GsaelibrarySearchAsync(new GsaelibrarySearchParams { Query = "radar" }),
            ["homestra_ad"] = () => _client.HomestraAdAsync(new HomestraAdParams { Query = "https://homestra.com/property/charming-4-bedroom-stone-house-with-historic-towers-in-idyllic-french-village/" }),
            ["homestra_search"] = () => _client.HomestraSearchAsync(new HomestraSearchParams { Query = "https://homestra.com/list/houses-for-sale/?maximum-price=900000&minimum-price=75000&page=1" }),
            ["huggingface_model"] = () => _client.HuggingfaceModelAsync(new HuggingfaceModelParams { Query = "google-bert/bert-base-uncased" }),
            ["huggingface_search"] = () => _client.HuggingfaceSearchAsync(new HuggingfaceSearchParams { Query = "bert" }),
            ["idealista_property"] = () => _client.IdealistaPropertyAsync(new IdealistaPropertyParams { Query = "https://www.idealista.com/fr/inmueble/111463799/" }),
            ["idealista_provinces"] = () => _client.IdealistaProvincesAsync(new IdealistaProvincesParams()),
            ["idealista_search"] = () => _client.IdealistaSearchAsync(new IdealistaSearchParams { Query = "https://www.idealista.com/venta-viviendas/barcelona/ciutat-vella/la-barceloneta/" }),
            ["idealo_product"] = () => _client.IdealoProductAsync(new IdealoProductParams { Query = "https://www.idealo.es/precios/205227870/asics-novablast-5-1011b974.html" }),
            ["idealo_search"] = () => _client.IdealoSearchAsync(new IdealoSearchParams { Query = "maison" }),
            ["ikea_product"] = () => _client.IkeaProductAsync(new IkeaProductParams { Query = "https://www.ikea.com/fr/fr/p/gladhoejden-bureau-assis-debout-blanc-30493939/" }),
            ["ikea_search"] = () => _client.IkeaSearchAsync(new IkeaSearchParams { Query = "https://www.ikea.com/fr/fr/search/?q=meuble" }),
            ["immobilienscout24_property"] = () => _client.Immobilienscout24PropertyAsync(new Immobilienscout24PropertyParams { Query = "168052908" }),
            ["immobilienscout24_search"] = () => _client.Immobilienscout24SearchAsync(new Immobilienscout24SearchParams { Query = "berlin/berlin" }),
            ["immoscout24ch_search"] = () => _client.Immoscout24chSearchAsync(new Immoscout24chSearchParams { Query = "zurich" }),
            ["immowelt_property"] = () => _client.ImmoweltPropertyAsync(new ImmoweltPropertyParams { Query = "https://www.immowelt.de/expose/65de0309-bf41-48ce-86fd-6a3441fd0487" }),
            ["immowelt_search"] = () => _client.ImmoweltSearchAsync(new ImmoweltSearchParams { Query = "https://www.immowelt.de/suche/mieten/wohnung/deutschland/ad02de1" }),
            ["imoveisweb_property"] = () => _client.ImoveiswebPropertyAsync(new ImoveiswebPropertyParams { Query = "https://www.imoveisweb.com.br/imoveis/casas/3151/" }),
            ["imoveisweb_search"] = () => _client.ImoveiswebSearchAsync(new ImoveiswebSearchParams { Query = "casas" }),
            ["indeed_company_info"] = () => _client.IndeedCompanyInfoAsync(new IndeedCompanyInfoParams { Query = "Proman-1" }),
            ["indeed_company_search"] = () => _client.IndeedCompanySearchAsync(new IndeedCompanySearchParams { Query = "airbus" }),
            ["indeed_job_info"] = () => _client.IndeedJobInfoAsync(new IndeedJobInfoParams { Query = "585181841cb618e0" }),
            ["indeed_job_salary"] = () => _client.IndeedJobSalaryAsync(new IndeedJobSalaryParams { Query = "ingénieur-etudes-et-développement" }),
            ["indeed_job_search"] = () => _client.IndeedJobSearchAsync(new IndeedJobSearchParams { Query = "developpeur python" }),
            ["inet_product"] = () => _client.InetProductAsync(new InetProductParams { Query = "4306287. A search link returns 400." }),
            ["inet_search"] = () => _client.InetSearchAsync(new InetSearchParams { Query = "test" }),
            ["instagram_post_info"] = () => _client.InstagramPostInfoAsync(new InstagramPostInfoParams { Query = "https://www.instagram.com/p/CshTQQlMGMl" }),
            ["instagram_user_info"] = () => _client.InstagramUserInfoAsync(new InstagramUserInfoParams { Query = "https://www.instagram.com/example-user/" }),
            ["intermarche_product"] = () => _client.IntermarcheProductAsync(new IntermarcheProductParams { Query = "https://www.intermarche.com/produit/flan-patissier-cuit-&-dore-au-four/3250391522828", ShopId = "11301" }),
            ["intermarche_search"] = () => _client.IntermarcheSearchAsync(new IntermarcheSearchParams { Query = "lait", ShopId = "11301" }),
            ["intersport_product"] = () => _client.IntersportProductAsync(new IntersportProductParams { Query = "https://www.intersport.fr/blancetvert-chaussures_de_running_homme_pegasus_plus2-nike-p-IM2541~VZE/" }),
            ["intersport_search"] = () => _client.IntersportSearchAsync(new IntersportSearchParams { Query = "nike" }),
            ["jamesedition_property"] = () => _client.JameseditionPropertyAsync(new JameseditionPropertyParams { Query = "test" }),
            ["jamesedition_search"] = () => _client.JameseditionSearchAsync(new JameseditionSearchParams { Query = "villa" }),
            ["jobs"] = () => _client.JobsAsync(new JobsParams { Id = "test" }),
            ["kick_user_info"] = () => _client.KickUserInfoAsync(new KickUserInfoParams { Query = "https://kick.com/example-user" }),
            ["kijiji_ad"] = () => _client.KijijiAdAsync(new KijijiAdParams { Query = "https://www.kijiji.ca/v-cell-phone-accessories/city-of-toronto/nateroso-iphone-16-case-cute-bear-honey-design/1745477275" }),
            ["kijiji_search"] = () => _client.KijijiSearchAsync(new KijijiSearchParams { Query = "https://www.kijiji.ca/b-bikes/city-of-toronto/c644l1700273" }),
            ["kroger_product"] = () => _client.KrogerProductAsync(new KrogerProductParams { Query = "0001111040101" }),
            ["kroger_search"] = () => _client.KrogerSearchAsync(new KrogerSearchParams { Query = "milk" }),
            ["lamudi_ad"] = () => _client.LamudiAdAsync(new LamudiAdParams { Query = "https://www.lamudi.com.ph/property/41032-73-626c638217d1-1d2a-1985488-907f-7802" }),
            ["lamudi_search"] = () => _client.LamudiSearchAsync(new LamudiSearchParams()),
            ["leboncoin_ad"] = () => _client.LeboncoinAdAsync(new LeboncoinAdParams { Query = "3012186547" }),
            ["leboncoin_search"] = () => _client.LeboncoinSearchAsync(new LeboncoinSearchParams { Query = "https://www.leboncoin.fr/c/offres_d_emploi?locations=Paris_75015__48.84105_2.29926_2747" }),
            ["leboncoin_search_api"] = () => _client.LeboncoinSearchApiAsync(new LeboncoinSearchApiParams()),
            ["lenovo_product"] = () => _client.LenovoProductAsync(new LenovoProductParams { Query = "https://www.lenovo.com/fr/fr/p/workstations/thinkstationp/thinkstation-p5-gen-2-intel-tower-workstation/len102s0024" }),
            ["lenovo_search"] = () => _client.LenovoSearchAsync(new LenovoSearchParams { Query = "thinkpad" }),
            ["leroymerlin_product"] = () => _client.LeroymerlinProductAsync(new LeroymerlinProductParams { Query = "https://www.leroymerlin.fr/produits/bobine-soudure-63378273.html" }),
            ["leroymerlin_search"] = () => _client.LeroymerlinSearchAsync(new LeroymerlinSearchParams { Query = "perceuse" }),
            ["lidl_product"] = () => _client.LidlProductAsync(new LidlProductParams { Query = "100397447" }),
            ["lidl_search"] = () => _client.LidlSearchAsync(new LidlSearchParams { Query = "perceuse" }),
            ["linkedin_company_info"] = () => _client.LinkedinCompanyInfoAsync(new LinkedinCompanyInfoParams()),
            ["linkedin_job_count"] = () => _client.LinkedinJobCountAsync(new LinkedinJobCountParams()),
            ["linkedin_job_info"] = () => _client.LinkedinJobInfoAsync(new LinkedinJobInfoParams { Query = "https://www.linkedin.com/jobs/view/4390885746" }),
            ["linkedin_job_search"] = () => _client.LinkedinJobSearchAsync(new LinkedinJobSearchParams()),
            ["linkedin_job_suggest"] = () => _client.LinkedinJobSuggestAsync(new LinkedinJobSuggestParams { Query = "Air" }),
            ["linkedin_post_info"] = () => _client.LinkedinPostInfoAsync(new LinkedinPostInfoParams { Query = "https://www.linkedin.com/posts/tanyalindsay_we-have-been-building-a-web3-gaming-platform-activity-7027826235833520128-m0GW" }),
            ["linkedin_product_info"] = () => _client.LinkedinProductInfoAsync(new LinkedinProductInfoParams { Query = "https://www.linkedin.com/products/15five" }),
            ["linkedin_profile_info"] = () => _client.LinkedinProfileInfoAsync(new LinkedinProfileInfoParams { Query = "https://www.linkedin.com/in/example-user" }),
            ["linkedinadlibrary_ad"] = () => _client.LinkedinadlibraryAdAsync(new LinkedinadlibraryAdParams { Query = "https://www.linkedin.com/ad-library/detail/1585536544" }),
            ["linkedinadlibrary_search"] = () => _client.LinkedinadlibrarySearchAsync(new LinkedinadlibrarySearchParams()),
            ["loopnet_property"] = () => _client.LoopnetPropertyAsync(new LoopnetPropertyParams { Query = "test" }),
            ["loopnet_search"] = () => _client.LoopnetSearchAsync(new LoopnetSearchParams { Query = "New York, NY" }),
            ["maersk_tracking"] = () => _client.MaerskTrackingAsync(new MaerskTrackingParams { Query = "MMAU1155207" }),
            ["manomano_product"] = () => _client.ManomanoProductAsync(new ManomanoProductParams { Query = "https://www.manomano.fr/p/perceuse-visseuse-lxt-makita-18-v-3-ah-84523637?product_id=158588345" }),
            ["manomano_product_offer"] = () => _client.ManomanoProductOfferAsync(new ManomanoProductOfferParams { Query = "158588345" }),
            ["manomano_search"] = () => _client.ManomanoSearchAsync(new ManomanoSearchParams { Query = "https://www.manomano.fr/perceuse-1146" }),
            ["mascus_ad"] = () => _client.MascusAdAsync(new MascusAdParams { Query = "https://www.mascus.fr/travaux-publics/broyeur-dechets/doppstadt-ak-435-profi/hocmgmfi.html" }),
            ["mascus_search"] = () => _client.MascusSearchAsync(new MascusSearchParams { Query = "https://www.mascus.be/bouw/graafmachines/be,country.html" }),
            ["mediamarkt_product"] = () => _client.MediamarktProductAsync(new MediamarktProductParams { Query = "https://www.mediamarkt.ch/fr/product/_apple-iphone-15-2203371.html" }),
            ["mediamarkt_search"] = () => _client.MediamarktSearchAsync(new MediamarktSearchParams { Query = "iphone" }),
            ["micromania_product"] = () => _client.MicromaniaProductAsync(new MicromaniaProductParams { Query = "162114" }),
            ["micromania_search"] = () => _client.MicromaniaSearchAsync(new MicromaniaSearchParams { Query = "zelda" }),
            ["mobile_ad"] = () => _client.MobileAdAsync(new MobileAdParams { Query = "455111824" }),
            ["mobile_search"] = () => _client.MobileSearchAsync(new MobileSearchParams { Query = "20100" }),
            ["morele_product"] = () => _client.MoreleProductAsync(new MoreleProductParams { Query = "https://www.morele.net/laptop-lenovo-thinkbook-15-iil-20sm000fpb-6721152/" }),
            ["morele_search"] = () => _client.MoreleSearchAsync(new MoreleSearchParams { Query = "laptop" }),
            ["mrbricolage_suggest"] = () => _client.MrbricolageSuggestAsync(new MrbricolageSuggestParams { Query = "marteau" }),
            ["newegg_product"] = () => _client.NeweggProductAsync(new NeweggProductParams { Query = "N82E16823126188" }),
            ["newegg_search"] = () => _client.NeweggSearchAsync(new NeweggSearchParams { Query = "logitech keyboard" }),
            ["northdata_company"] = () => _client.NorthdataCompanyAsync(new NorthdataCompanyParams { Query = "Maison Boinaud" }),
            ["northdata_search"] = () => _client.NorthdataSearchAsync(new NorthdataSearchParams { Query = "BMW" }),
            ["npi_provider"] = () => _client.NpiProviderAsync(new NpiProviderParams { Query = "test" }),
            ["npi_search"] = () => _client.NpiSearchAsync(new NpiSearchParams()),
            ["oceanio_search"] = () => _client.OceanioSearchAsync(new OceanioSearchParams { Query = "google.com" }),
            ["olx_ad"] = () => _client.OlxAdAsync(new OlxAdParams { Query = "https://www.olx.pl/d/oferta/apple-iphone-17-pro-256-gb-nowy-szczecin-zaplombowany-karta-sim-ul-baluki-4-szczecin-CID99-ID18Acsd.html" }),
            ["olx_product"] = () => _client.OlxProductAsync(new OlxProductParams { Query = "https://www.olx.com.br/eletronicos-e-celulares/celulares-e-smartphones/iphone-14-128gb-como-novo-IDJ4mBb.html" }),
            ["olx_search"] = () => _client.OlxSearchAsync(new OlxSearchParams { Query = "iphone 14" }),
            ["oscaro_product"] = () => _client.OscaroProductAsync(new OscaroProductParams { Query = "https://www.oscaro.com/huile-moteur-motul-102208-15213891-1862-p" }),
            ["oscaro_search"] = () => _client.OscaroSearchAsync(new OscaroSearchParams { Query = "moteur ai" }),
            ["ovoko_ad"] = () => _client.OvokoAdAsync(new OvokoAdParams { Query = "https://ovoko.fr/les-pieces/lgn634714-1780a036-mitsubishi-asx-filtre-d-habitacle" }),
            ["ovoko_search"] = () => _client.OvokoSearchAsync(new OvokoSearchParams { Query = "https://ovoko.pl/szukaj?q=Porsche+Macan+uszczelka+klapy+bagaznika&exact=1" }),
            ["owler_company_info"] = () => _client.OwlerCompanyInfoAsync(new OwlerCompanyInfoParams { Query = "https://owler.com/company/tesla" }),
            ["owler_search"] = () => _client.OwlerSearchAsync(new OwlerSearchParams { Query = "tesla" }),
            ["pagesjaunes_page_info"] = () => _client.PagesjaunesPageInfoAsync(new PagesjaunesPageInfoParams { Query = "https://www.pagesjaunes.fr/pros/56824689" }),
            ["pagesjaunes_search"] = () => _client.PagesjaunesSearchAsync(new PagesjaunesSearchParams { Query = "https://www.pagesjaunes.fr/annuaire/chercherlespros?quoiqui=Dentiste&ou=Toulouse+(31000)&univers=pagesjaunes&idOu=" }),
            ["patreon_profile_info"] = () => _client.PatreonProfileInfoAsync(new PatreonProfileInfoParams { Query = "https://www.patreon.com/example-user" }),
            ["patreon_search"] = () => _client.PatreonSearchAsync(new PatreonSearchParams { Query = "creator" }),
            ["petsmart_product"] = () => _client.PetsmartProductAsync(new PetsmartProductParams { Query = "https://www.petsmart.com/dog/food/dry-food/purina-pro-plan-sensitive-skin-and-stomach-dry-dog-food-adult-salmon-and-rice-formula-digestive-health-36648.html" }),
            ["petsmart_search"] = () => _client.PetsmartSearchAsync(new PetsmartSearchParams { Query = "dog food" }),
            ["picard_product"] = () => _client.PicardProductAsync(new PicardProductParams { Query = "050314" }),
            ["picard_search"] = () => _client.PicardSearchAsync(new PicardSearchParams { Query = "patate douce" }),
            ["pinterest_user_info"] = () => _client.PinterestUserInfoAsync(new PinterestUserInfoParams { Query = "https://www.pinterest.com/username" }),
            ["pitchbook_company_info"] = () => _client.PitchbookCompanyInfoAsync(new PitchbookCompanyInfoParams { Query = "11062-99" }),
            ["pitchbook_search"] = () => _client.PitchbookSearchAsync(new PitchbookSearchParams { Query = "airbus" }),
            ["pointp_product"] = () => _client.PointpProductAsync(new PointpProductParams { Query = "https://www.pointp.fr/p/platre-isolation-ite/laine-de-verre-a-souffler-thermo-loft-knauf-insulation-sac-16-A3334230" }),
            ["pointp_search"] = () => _client.PointpSearchAsync(new PointpSearchParams { Query = "colle" }),
            ["prnewswire_article"] = () => _client.PrnewswireArticleAsync(new PrnewswireArticleParams { Query = "test" }),
            ["prnewswire_search"] = () => _client.PrnewswireSearchAsync(new PrnewswireSearchParams { Query = "apple" }),
            ["producthunt_product_info"] = () => _client.ProducthuntProductInfoAsync(new ProducthuntProductInfoParams { Query = "https://www.producthunt.com/products/miro" }),
            ["proshop_product"] = () => _client.ProshopProductAsync(new ProshopProductParams { Query = "3071602" }),
            ["proshop_search"] = () => _client.ProshopSearchAsync(new ProshopSearchParams { Query = "ssd" }),
            ["rappi_product"] = () => _client.RappiProductAsync(new RappiProductParams { Query = "test" }),
            ["rappi_search"] = () => _client.RappiSearchAsync(new RappiSearchParams { Query = "leche" }),
            ["ratemyprofessors_professor"] = () => _client.RatemyprofessorsProfessorAsync(new RatemyprofessorsProfessorParams { Query = "1064107" }),
            ["ratemyprofessors_search"] = () => _client.RatemyprofessorsSearchAsync(new RatemyprofessorsSearchParams { Query = "smith" }),
            ["realestatecomau_search"] = () => _client.RealestatecomauSearchAsync(new RealestatecomauSearchParams { Query = "Sydney, NSW" }),
            ["realtor_property"] = () => _client.RealtorPropertyAsync(new RealtorPropertyParams { Query = "https://www.realtor.com/realestateandhomes-detail/1492-E-57th-St_Brooklyn_NY_11234_M46117-61190" }),
            ["realtor_search"] = () => _client.RealtorSearchAsync(new RealtorSearchParams { Query = "https://www.realtor.com/realestateandhomes-search/New-York_NY" }),
            ["redfin_property"] = () => _client.RedfinPropertyAsync(new RedfinPropertyParams { Query = "https://www.redfin.com/WA/Seattle/1120-8th-Ave-98101/unit-1603/home/14602?region_id=16163&region_type=6" }),
            ["redfin_search"] = () => _client.RedfinSearchAsync(new RedfinSearchParams { Query = "https://www.redfin.com/city/16163/WA/Seattle" }),
            ["reichelt_product"] = () => _client.ReicheltProductAsync(new ReicheltProductParams { Query = "test" }),
            ["reichelt_search"] = () => _client.ReicheltSearchAsync(new ReicheltSearchParams { Query = "test" }),
            ["reverb_product"] = () => _client.ReverbProductAsync(new ReverbProductParams { Query = "96933467" }),
            ["reverb_search"] = () => _client.ReverbSearchAsync(new ReverbSearchParams { Query = "guitar" }),
            ["rexel_product"] = () => _client.RexelProductAsync(new RexelProductParams { Query = "https://www.rexel.fr/frx/HAGWXF122/p/71742449" }),
            ["rexel_search"] = () => _client.RexelSearchAsync(new RexelSearchParams { Query = "prise" }),
            ["rightmove_property"] = () => _client.RightmovePropertyAsync(new RightmovePropertyParams { Query = "https://www.rightmove.co.uk/properties/172438235" }),
            ["rightmove_search"] = () => _client.RightmoveSearchAsync(new RightmoveSearchParams { Query = "Bristol" }),
            ["rsonline_product"] = () => _client.RsonlineProductAsync(new RsonlineProductParams { Query = "https://uk.rs-online.com/web/p/splice-connectors/0303327" }),
            ["rsonline_search"] = () => _client.RsonlineSearchAsync(new RsonlineSearchParams { Query = "connector" }),
            ["seek_job"] = () => _client.SeekJobAsync(new SeekJobParams { Query = "test" }),
            ["seek_search"] = () => _client.SeekSearchAsync(new SeekSearchParams { Query = "nurse" }),
            ["seloger_agency"] = () => _client.SelogerAgencyAsync(new SelogerAgencyParams { Query = "https://www.seloger.com/professionnels/agences-immobilieres/castanet-tolosan-31320/agence-21719" }),
            ["seloger_agency_rent"] = () => _client.SelogerAgencyRentAsync(new SelogerAgencyRentParams { Query = "18317" }),
            ["seloger_agency_sale"] = () => _client.SelogerAgencySaleAsync(new SelogerAgencySaleParams { Query = "18317" }),
            ["seloger_property"] = () => _client.SelogerPropertyAsync(new SelogerPropertyParams { Query = "https://www.seloger.com/annonces/achat/appartement/bordeaux-33/capucins-saint-michel-nansouty-saint-genes/234462129.htm" }),
            ["seloger_search"] = () => _client.SelogerSearchAsync(new SelogerSearchParams { Query = "https://www.seloger.com/annonces/achat/appartement/bordeaux-33/" }),
            ["sephora_product"] = () => _client.SephoraProductAsync(new SephoraProductParams { Query = "https://www.sephora.fr/p/midnight-espresso---eau-de-parfum-au-cafe-831955.html" }),
            ["sephora_search"] = () => _client.SephoraSearchAsync(new SephoraSearchParams { Query = "mascara" }),
            ["shophouzz_product"] = () => _client.ShophouzzProductAsync(new ShophouzzProductParams { Query = "https://shophouzz.com/products/burke-counter-stool-cognac-vegan-leather-white-oak-veneer-metal-dowel-prvw-vr-186041078" }),
            ["shopify_apps"] = () => _client.ShopifyAppsAsync(new ShopifyAppsParams { Query = "https://apps.shopify.com/judgeme" }),
            ["shopify_catalog"] = () => _client.ShopifyCatalogAsync(new ShopifyCatalogParams { Query = "https://www.decathlon.com" }),
            ["shopify_collections"] = () => _client.ShopifyCollectionsAsync(new ShopifyCollectionsParams { Query = "https://www.decathlon.com" }),
            ["shopify_product"] = () => _client.ShopifyProductAsync(new ShopifyProductParams { Query = "https://www.decathlon.com/products/simond-backpacking-sleeping-bag-mt500-41-f-synthetic-346446" }),
            ["similarweb_domain"] = () => _client.SimilarwebDomainAsync(new SimilarwebDomainParams { Query = "google.com" }),
            ["similarweb_search"] = () => _client.SimilarwebSearchAsync(new SimilarwebSearchParams { Query = "google.com" }),
            ["snapeda_product"] = () => _client.SnapedaProductAsync(new SnapedaProductParams { Query = "CYPD3175-24LQXQ/Cypress Semiconductor" }),
            ["snapeda_search"] = () => _client.SnapedaSearchAsync(new SnapedaSearchParams { Query = "USB Type C" }),
            ["starbucks_menu"] = () => _client.StarbucksMenuAsync(new StarbucksMenuParams()),
            ["starbucks_product"] = () => _client.StarbucksProductAsync(new StarbucksProductParams { Query = "418/hot" }),
            ["starbucks_search"] = () => _client.StarbucksSearchAsync(new StarbucksSearchParams()),
            ["stats"] = () => _client.StatsAsync(new StatsParams()),
            ["stockx_product"] = () => _client.StockxProductAsync(new StockxProductParams { Query = "nike-air-max-1-travis-scott-wheat" }),
            ["stockx_search"] = () => _client.StockxSearchAsync(new StockxSearchParams { Query = "Air Jordan Retro" }),
            ["stockx_trends"] = () => _client.StockxTrendsAsync(new StockxTrendsParams { Query = "sneakers" }),
            ["stubhub_event"] = () => _client.StubhubEventAsync(new StubhubEventParams { Query = "test" }),
            ["stubhub_search"] = () => _client.StubhubSearchAsync(new StubhubSearchParams { Query = "nba" }),
            ["target_product"] = () => _client.TargetProductAsync(new TargetProductParams { Query = "17457624" }),
            ["target_search"] = () => _client.TargetSearchAsync(new TargetSearchParams { Query = "shampoo" }),
            ["tdlr_license"] = () => _client.TdlrLicenseAsync(new TdlrLicenseParams { Query = "test" }),
            ["tdlr_search"] = () => _client.TdlrSearchAsync(new TdlrSearchParams()),
            ["ted_notice"] = () => _client.TedNoticeAsync(new TedNoticeParams { Query = "30-2026" }),
            ["ted_search"] = () => _client.TedSearchAsync(new TedSearchParams()),
            ["thalia_product"] = () => _client.ThaliaProductAsync(new ThaliaProductParams { Query = "A1074644377" }),
            ["thalia_search"] = () => _client.ThaliaSearchAsync(new ThaliaSearchParams { Query = "Harry Potter" }),
            ["thefork_restaurant"] = () => _client.TheforkRestaurantAsync(new TheforkRestaurantParams { Query = "https://www.thefork.fr/restaurant/salamboo-amilkar-r10892#date=2026-06-13&rankedBy=SEARCH_ENGINE" }),
            ["thefork_search"] = () => _client.TheforkSearchAsync(new TheforkSearchParams { Query = "https://www.thefork.fr/search?cityId=529135&date=2026-06-13&hour=660&partySize=8&timezone=Europe%2FParis" }),
            ["thomasnet_company"] = () => _client.ThomasnetCompanyAsync(new ThomasnetCompanyParams { Query = "https://www.thomasnet.com/company/zepole-supply-co-20088400/profile" }),
            ["thomasnet_search"] = () => _client.ThomasnetSearchAsync(new ThomasnetSearchParams { Query = "https://www.thomasnet.com/suppliers/search?searchterm=Table+Forks" }),
            ["ticketmaster_event"] = () => _client.TicketmasterEventAsync(new TicketmasterEventParams { Query = "test" }),
            ["ticketmaster_search"] = () => _client.TicketmasterSearchAsync(new TicketmasterSearchParams { Query = "jazz" }),
            ["tiktoktopads_ad"] = () => _client.TiktoktopadsAdAsync(new TiktoktopadsAdParams { Query = "https://ads.tiktok.com/business/creativecenter/topads/7686267188656750612/pc/en?period=30" }),
            ["tiktoktopads_search"] = () => _client.TiktoktopadsSearchAsync(new TiktoktopadsSearchParams()),
            ["tradeaplane_listing"] = () => _client.TradeaplaneListingAsync(new TradeaplaneListingParams { Query = "https://www.trade-a-plane.com/search?category_level1=Piston+Engine&make=ROLLS+ROYCE&model=PACKARD+V-1650&listing_id=2419556&s-type=engine" }),
            ["tradeaplane_search"] = () => _client.TradeaplaneSearchAsync(new TradeaplaneSearchParams { Query = "rol" }),
            ["trustpilot_company_info"] = () => _client.TrustpilotCompanyInfoAsync(new TrustpilotCompanyInfoParams { Query = "https://www.trustpilot.com/review/nordvpn.com" }),
            ["untappd_beer"] = () => _client.UntappdBeerAsync(new UntappdBeerParams { Query = "4509" }),
            ["untappd_search"] = () => _client.UntappdSearchAsync(new UntappdSearchParams { Query = "ipa" }),
            ["upwork_freelancer"] = () => _client.UpworkFreelancerAsync(new UpworkFreelancerParams { Query = "~0123456789abcdef01" }),
            ["upwork_jobs"] = () => _client.UpworkJobsAsync(new UpworkJobsParams { Query = "api-development" }),
            ["upwork_services"] = () => _client.UpworkServicesAsync(new UpworkServicesParams { Query = "mobile-apps" }),
            ["upwork_services_info"] = () => _client.UpworkServicesInfoAsync(new UpworkServicesInfoParams { Query = "https://www.upwork.com/services/product/development-it-mobile-app-developer-android-ios-flutter-application-hybrid-apps-1403673770703212544" }),
            ["usage"] = () => _client.UsageAsync(),
            ["usaspending_award"] = () => _client.UsaspendingAwardAsync(new UsaspendingAwardParams { Query = "CONT_AWD_GSFC0198106DNAS526555_8000_-NONE-_-NONE-." }),
            ["usaspending_search"] = () => _client.UsaspendingSearchAsync(new UsaspendingSearchParams { Query = "software. An award URL returns 400." }),
            ["vestiairecollective_product"] = () => _client.VestiairecollectiveProductAsync(new VestiairecollectiveProductParams { Query = "https://www.vestiairecollective.com/vetements-femme/vestes/chanel/vesteblouson-chanel-la-petite-veste-noire-en-tweed-rose-73024488.shtml" }),
            ["vestiairecollective_search"] = () => _client.VestiairecollectiveSearchAsync(new VestiairecollectiveSearchParams { Query = "chanel" }),
            ["vinted_item"] = () => _client.VintedItemAsync(new VintedItemParams { Query = "https://www.vinted.com/items/9437210920-purple-prom-dress" }),
            ["vinted_search"] = () => _client.VintedSearchAsync(new VintedSearchParams { Query = "nike" }),
            ["vinted_user_info"] = () => _client.VintedUserInfoAsync(new VintedUserInfoParams { Query = "https://www.vinted.fr/member/123456789-username" }),
            ["vivareal_property"] = () => _client.VivarealPropertyAsync(new VivarealPropertyParams { Query = "https://www.vivareal.com.br/imovel/apartamento-3-quartos-brooklin-zona-sul-sao-paulo-com-garagem-186m2-venda-RS3710000-id-2776423622/" }),
            ["vivareal_search"] = () => _client.VivarealSearchAsync(new VivarealSearchParams { Query = "São Paulo" }),
            ["walmart_product"] = () => _client.WalmartProductAsync(new WalmartProductParams { Query = "https://www.walmart.com/ip/Fresh-Rockit-Crisp-Sweet-Miniature-Apples-3lb-Tub/309762096" }),
            ["walmart_search"] = () => _client.WalmartSearchAsync(new WalmartSearchParams { Query = "https://www.walmart.com/search?q=apples" }),
            ["walmart_suggest"] = () => _client.WalmartSuggestAsync(new WalmartSuggestParams { Query = "apples" }),
            ["website_antibot"] = () => _client.WebsiteAntibotAsync(new WebsiteAntibotParams { Query = "https://www.example.com" }),
            ["website_crawler"] = () => _client.WebsiteCrawlerAsync(new WebsiteCrawlerParams { Query = "https://example.com" }),
            ["website_email_phone_extractor"] = () => _client.WebsiteEmailPhoneExtractorAsync(new WebsiteEmailPhoneExtractorParams { Query = "https://www.hexa.cc" }),
            ["website_rendering"] = () => _client.WebsiteRenderingAsync(new WebsiteRenderingParams { Query = "https://example.com" }),
            ["website_screenshot"] = () => _client.WebsiteScreenshotAsync(new WebsiteScreenshotParams { Url = "https://www.amazon.com/s?k=pokemon" }),
            ["website_technology"] = () => _client.WebsiteTechnologyAsync(new WebsiteTechnologyParams { Query = "https://example.com" }),
            ["website_webunlocker"] = () => _client.WebsiteWebunlockerAsync(new WebsiteWebunlockerParams { Query = "https://www.norauto.fr/t/pneu/w-185-h-55-r-14/ete-s/80-l-h-q.html" }),
            ["weibo_post_info"] = () => _client.WeiboPostInfoAsync(new WeiboPostInfoParams { Query = "test" }),
            ["weibo_search"] = () => _client.WeiboSearchAsync(new WeiboSearchParams { Query = "test" }),
            ["welcometothejungle_company"] = () => _client.WelcometothejungleCompanyAsync(new WelcometothejungleCompanyParams { Query = "alan" }),
            ["welcometothejungle_search"] = () => _client.WelcometothejungleSearchAsync(new WelcometothejungleSearchParams { Query = "python" }),
            ["wellfound_company_info"] = () => _client.WellfoundCompanyInfoAsync(new WellfoundCompanyInfoParams { Query = "https://wellfound.com/company/dataiku" }),
            ["x_post_info"] = () => _client.XPostInfoAsync(new XPostInfoParams { Query = "https://x.com/example-user/status/0000000000000000001" }),
            ["x_profile_info"] = () => _client.XProfileInfoAsync(new XProfileInfoParams { Query = "elonmusk" }),
            ["yahoo_search"] = () => _client.YahooSearchAsync(new YahooSearchParams { Query = "test" }),
            ["yellowpages_business"] = () => _client.YellowpagesBusinessAsync(new YellowpagesBusinessParams { Query = "https://www.yellowpages.com/los-angeles-ca/mip/anawalt-lumber-13945594?lid=1002108313121" }),
            ["yellowpages_search"] = () => _client.YellowpagesSearchAsync(new YellowpagesSearchParams { Query = "https://www.yellowpages.com/los-angeles-ca/plumbers" }),
            ["yelp_business"] = () => _client.YelpBusinessAsync(new YelpBusinessParams { Query = "https://www.yelp.com/biz/novela-san-francisco-2" }),
            ["yelp_reviews"] = () => _client.YelpReviewsAsync(new YelpReviewsParams { Query = "cX5dOMkwZ5rqWbeIJpw_Ig" }),
            ["yelp_search"] = () => _client.YelpSearchAsync(new YelpSearchParams { Query = "https://www.yelp.com/search?find_desc=Cocktail+Bars&find_loc=San+Francisco%2C+CA" }),
            ["zapimoveis_ad"] = () => _client.ZapimoveisAdAsync(new ZapimoveisAdParams { Query = "https://www.zapimoveis.com.br/imovel/venda-apartamento-3-quartos-mobiliado-jardim-paulista-zona-sul-sao-paulo-sp-170m2-id-2912096343/" }),
            ["zapimoveis_search"] = () => _client.ZapimoveisSearchAsync(new ZapimoveisSearchParams { Query = "São Paulo" }),
            ["zazzle_product"] = () => _client.ZazzleProductAsync(new ZazzleProductParams { Query = "https://www.zazzle.com/branded_logo_two_tone_coffee_mug-256528848440338952." }),
            ["zazzle_search"] = () => _client.ZazzleSearchAsync(new ZazzleSearchParams { Query = "coffee mug. A product URL returns 400." }),
            ["zillow_property"] = () => _client.ZillowPropertyAsync(new ZillowPropertyParams { Query = "https://www.zillow.com/homedetails/20-Kyle-Ct-Staten-Island-NY-10312/32361983_zpid/" }),
            ["zillow_search"] = () => _client.ZillowSearchAsync(new ZillowSearchParams { Query = "https://www.zillow.com/staten-island-ny/" }),
            ["zonaprop_property"] = () => _client.ZonapropPropertyAsync(new ZonapropPropertyParams { Query = "https://www.zonaprop.com.ar/propiedades/clasificado/veclapin-venta-departamento-2-ambientes-villa-crespo-59096735.html" }),
            ["zonaprop_search"] = () => _client.ZonapropSearchAsync(new ZonapropSearchParams { Query = "capital federal" }),
            ["zoopla_property"] = () => _client.ZooplaPropertyAsync(new ZooplaPropertyParams { Query = "https://www.zoopla.co.uk/to-rent/details/73119609/" }),
            ["zoopla_search"] = () => _client.ZooplaSearchAsync(new ZooplaSearchParams { Query = "https://www.zoopla.co.uk/for-sale/property/manchester/" }),
            ["zooplus_product"] = () => _client.ZooplusProductAsync(new ZooplusProductParams { Query = "705321" }),
            ["zooplus_search"] = () => _client.ZooplusSearchAsync(new ZooplusSearchParams { Query = "croquettes" }),
            ["zoro_product"] = () => _client.ZoroProductAsync(new ZoroProductParams { Query = "https://www.zoro.com/3m-safety-glasses-securefit-200-series-scotchgard-anti-fog-frameless-blue-temples-clear-lens-sf201sgaf-blu/i/G1937045/" }),
            ["zoro_search"] = () => _client.ZoroSearchAsync(new ZoroSearchParams { Query = "safety glasses" }),
        };
        foreach (var (name, endpoint) in Endpoints.All)
        {
            await calls[name]();
            var request = _handler.Requests[^1].Request;
            Assert.Equal("https://api.piloterr.com" + endpoint.Path.Replace("{id}", "test"), request.RequestUri!.GetLeftPart(UriPartial.Path));
            Assert.Equal(endpoint.Methods[0], request.Method.Method);
        }
        Assert.Equal(Endpoints.All.Count, calls.Count);
    }

    [Fact]
    public async Task BinaryResponse()
    {
        _handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }) { Headers = { { "Content-Type", "image/png" } } },
        };
        var response = await _client.RequestAsync("GET", "/v1/anything");
        Assert.False(response.IsJson);
        Assert.Equal(4, response.Bytes.Length);
        Assert.Equal("image/png", response.ContentType);
    }

    [Fact]
    public async Task NonJsonSuccessForJsonOperation()
    {
        _handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>", Encoding.UTF8, "text/html") };
        await Assert.ThrowsAsync<PiloterrResponseDecodingException>(() => _client.AmazonProductAsync(new AmazonProductParams { Query = "B09DFCB66S" }));
    }

    [Fact]
    public async Task ApiError()
    {
        _handler.Responder = _ => Json(401, "{\"error\":\"Invalid API Key\"}");
        var error = await Assert.ThrowsAsync<PiloterrApiException>(() => _client.RequestAsync("GET", "/v1/usage"));
        Assert.Equal(401, error.StatusCode);
        Assert.Equal("Invalid API Key", error.Message);
        Assert.Equal("Invalid API Key", error.ResponseBody!.Value.GetProperty("error").GetString());
    }

    [Fact]
    public async Task NonJsonErrorBody()
    {
        _handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("Bad gateway") };
        var error = await Assert.ThrowsAsync<PiloterrApiException>(
            () => _client.RequestAsync("GET", "/v1/usage", null, new RequestOptions { MaxRetries = 0 }));
        Assert.Equal(502, error.StatusCode);
        Assert.Equal("Bad gateway", Encoding.UTF8.GetString(error.RawResponse.Body));
    }

    [Fact]
    public async Task TransportError()
    {
        _handler.Responder = _ => throw new HttpRequestException("network down");
        await Assert.ThrowsAsync<PiloterrTransportException>(() => _client.RequestAsync("GET", "/v1/usage"));
    }

    [Fact]
    public async Task DeserializesIntoTypes()
    {
        _handler.Responder = _ => Json(200, "{\"value\":42}");
        var response = await _client.RequestAsync("GET", "/v1/usage");
        Assert.Equal(42, response.Deserialize<Payload>()!.Value);
    }

    private sealed record Payload(int Value);

    [Fact]
    public async Task RetriesThenSucceeds()
    {
        var calls = 0;
        _handler.Responder = _ =>
        {
            calls++;
            if (calls == 1)
            {
                throw new HttpRequestException("reset");
            }
            if (calls == 2)
            {
                var busy = Json(429, "{}");
                busy.Headers.TryAddWithoutValidation("Retry-After", "0");
                return busy;
            }
            return calls == 3 ? Json(503, "{}") : Json(200, "{}");
        };
        var response = await _client.RequestAsync("GET", "/v1/usage");
        Assert.True(response.IsJson);
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task RetryLimitsAndClientErrors()
    {
        using var limited = new PiloterrClient(new PiloterrClientOptions
        {
            ApiKey = "k",
            HttpClient = new HttpClient(_handler),
            MaxRetries = 2,
            RetryDelay = TimeSpan.FromMilliseconds(1),
        });
        _handler.Responder = _ => Json(503, "{}");
        await Assert.ThrowsAsync<PiloterrApiException>(() => limited.RequestAsync("GET", "/v1/usage"));
        Assert.Equal(3, _handler.Requests.Count);
        _handler.Responder = _ => Json(402, "{}");
        await Assert.ThrowsAsync<PiloterrApiException>(() => limited.RequestAsync("GET", "/v1/usage"));
        Assert.Equal(4, _handler.Requests.Count);
        _handler.Responder = _ => Json(500, "{}");
        await Assert.ThrowsAsync<PiloterrApiException>(
            () => limited.RequestAsync("GET", "/v1/usage", null, new RequestOptions { MaxRetries = 0 }));
        Assert.Equal(5, _handler.Requests.Count);
    }

    [Theory]
    [InlineData("Rate limit exceeded: You've exceeded the rate limit on your subscription", 2)]
    [InlineData("Rate limit exceeded for the API key: quota daily", 1)]
    [InlineData("Invalid API Key", 1)]
    public async Task RateLimitsAreRetriedButNotQuotas(string message, int expectedCalls)
    {
        var calls = 0;
        _handler.Responder = _ => ++calls == 1 ? Json(401, JsonSerializer.Serialize(new { error = message })) : Json(200, "{}");
        try
        {
            await _client.RequestAsync("GET", "/v1/usage");
        }
        catch (PiloterrApiException)
        {
        }
        Assert.Equal(expectedCalls, calls);
    }

    [Fact]
    public async Task AcceptsGlobalParameters()
    {
        await _client.AmazonProductAsync(new AmazonProductParams { Query = "B09DFCB66S", Fields = "a,b.c", IsAsync = true, SoftFail = false });
        var query = _handler.Requests[0].Request.RequestUri!.Query;
        Assert.Contains("fields=a%2Cb.c", query);
        Assert.Contains("is_async=true", query);
        Assert.Contains("soft_fail=false", query);
    }

    [Fact]
    public async Task BatchKeepsOrderAndIsolatesFailures()
    {
        var running = 0;
        var peak = 0;
        var results = await _client.BatchAsync(Enumerable.Range(1, 6), async (n, ct) =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            await Task.Delay(5, ct);
            Interlocked.Decrement(ref running);
            if (n == 4)
            {
                throw new InvalidOperationException("boom");
            }
            return n * 10;
        }, concurrency: 2);
        Assert.Equal(2, peak);
        Assert.Equal(6, results.Count);
        Assert.False(results[3].Ok);
        Assert.Equal(60, results[5].Value);
        Assert.Equal(3, results[3].Index);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    [Fact]
    public async Task WaitForJob()
    {
        var statuses = new Queue<string>(new[] { "Queued", "Running", "CompletedJob" });
        _handler.Responder = _ => Json(200, "{\"status\":\"" + statuses.Dequeue() + "\",\"progress\":50}");
        var seen = 0;
        var job = await _client.WaitForJobAsync("abc", TimeSpan.FromMilliseconds(1), TimeSpan.FromMinutes(1), _ => seen++);
        Assert.Equal("CompletedJob", job.Json.GetProperty("status").GetString());
        Assert.Equal(3, _handler.Requests.Count);
        Assert.Equal("/v1/jobs/abc", _handler.Requests[0].Request.RequestUri!.AbsolutePath);
        Assert.Equal(3, seen);
    }

    [Fact]
    public async Task WaitForJobTimesOut()
    {
        _handler.Responder = _ => Json(200, "{\"status\":\"Running\"}");
        await Assert.ThrowsAsync<PiloterrJobTimeoutException>(
            () => _client.WaitForJobAsync("abc", TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(1)));
    }
}
