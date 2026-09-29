import type { components } from './schema';

type Schemas = components['schemas'];

/** Public discovery contracts (generated from the API's OpenAPI document; never hand-written). */
export type ShopSearchItem = Schemas['ShopSearchItemResponse'];
export type ShopSearchResult = Schemas['ShopSearchResponse'];
export type DiscoveryOffer = Schemas['DiscoveryOfferResponse'];
export type PublicShop = Schemas['PublicShopResponse'];
export type PublicShopStatus = Schemas['PublicShopStatusResponse'];
export type PublicService = Schemas['PublicServiceResponse'];
export type PublicPackage = Schemas['PublicPackageResponse'];
export type PublicProfessional = Schemas['PublicProfessionalResponse'];
export type PublicProfessionalDetail = Schemas['PublicProfessionalDetailResponse'];
export type ProfessionalNextSlots = Schemas['ProfessionalNextSlotsResponse'];
export type PublicReviews = Schemas['PublicReviewsResponse'];
export type PublicReview = Schemas['PublicReviewResponse'];
export type PopularCategory = Schemas['PopularCategoryResponse'];
export type ServiceCategory = Schemas['ServiceCategoryResponse'];
export type DiscoveryAreas = Schemas['DiscoveryAreasResponse'];
export type DiscoveryArea = Schemas['DiscoveryAreaResponse'];
export type DiscoveryStats = Schemas['DiscoveryStatsResponse'];
export type TopProfessional = Schemas['TopProfessionalResponse'];
export type OpeningInterval = Schemas['OpeningIntervalResponse'];
