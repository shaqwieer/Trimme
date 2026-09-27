'use client';

import { browserApi } from '@/lib/api/client';
import { ensureOk } from '@/lib/api/errors';
import type { components } from '@/lib/api/schema';

type Schemas = components['schemas'];
export type ShopCategory = Schemas['ShopCategory'];
export type ShopAmenity = Schemas['ShopAmenity'];
export type ShopProfileField = Schemas['ShopProfileField'];
export type ShopImage = Schemas['ShopImageResponse'];
export type ShopLocation = Schemas['ShopLocationResponse'];

export const SHOP_CATEGORIES: ShopCategory[] = ['Barbershop', 'Salon', 'Unisex'];
export const SHOP_AMENITIES: ShopAmenity[] = [
  'Parking',
  'WiFi',
  'KidsFriendly',
  'WheelchairAccessible',
  'WaitingArea',
  'PrayerArea',
];
export const PROFILE_FIELDS: ShopProfileField[] = [
  'Name',
  'Description',
  'Category',
  'PublicPhone',
  'Amenities',
  'Logo',
  'Cover',
  'Gallery',
  'Location',
];

/** The profile shape both the admin detail and the shop's own profile share. */
export type ShopProfileData = Pick<
  Schemas['ShopOwnProfileResponse'],
  | 'nameAr'
  | 'nameEn'
  | 'descriptionAr'
  | 'descriptionEn'
  | 'category'
  | 'publicPhone'
  | 'amenities'
  | 'isVerified'
  | 'logoUrl'
  | 'coverUrl'
  | 'gallery'
  | 'location'
  | 'editableFields'
  | 'version'
>;

export type ProfileText = Omit<Schemas['UpdateOwnShopProfileRequest'], 'version'>;

/** Who is editing: a platform admin (any shop, every field) or the shop itself (its own shop, within the policy). */
export type EditorMode = { kind: 'admin'; shopId: string } | { kind: 'shop' };

export type ImageSlot = 'logo' | 'cover';

function form(file: File) {
  const body = new FormData();
  body.append('file', file);
  // openapi-fetch sends FormData as is, and the browser sets the multipart boundary.
  return body as unknown as { file?: string };
}

/** The write calls of the profile screens, for either editor. Each throws an ApiError on failure. */
export type ShopEditApi = {
  /** `isVerified` is admin-only; the shop's own call ignores it. */
  saveProfile: (text: ProfileText, version: number, isVerified?: boolean) => Promise<unknown>;
  upload: (slot: ImageSlot, file: File) => Promise<unknown>;
  remove: (slot: ImageSlot) => Promise<unknown>;
  addToGallery: (file: File) => Promise<unknown>;
  removeFromGallery: (mediaId: string) => Promise<unknown>;
  saveLocation: (location: Schemas['ShopLocationRequest']) => Promise<unknown>;
};

export function shopApi(mode: EditorMode): ShopEditApi {
  if (mode.kind === 'admin') {
    const path = { params: { path: { shopId: mode.shopId } } };
    return {
      saveProfile: async (text, version, isVerified = false) =>
        ensureOk(
          await browserApi.PUT('/api/v1/admin/shops/{shopId}', {
            ...path,
            body: { ...text, isVerified, version },
          }),
        ),
      upload: async (slot: ImageSlot, file: File) =>
        ensureOk(
          slot === 'logo'
            ? await browserApi.PUT('/api/v1/admin/shops/{shopId}/logo', { ...path, body: form(file) })
            : await browserApi.PUT('/api/v1/admin/shops/{shopId}/cover', { ...path, body: form(file) }),
        ),
      remove: async (slot: ImageSlot) =>
        ensureOk(
          slot === 'logo'
            ? await browserApi.DELETE('/api/v1/admin/shops/{shopId}/logo', path)
            : await browserApi.DELETE('/api/v1/admin/shops/{shopId}/cover', path),
        ),
      addToGallery: async (file: File) =>
        ensureOk(
          await browserApi.POST('/api/v1/admin/shops/{shopId}/gallery', { ...path, body: form(file) }),
        ),
      removeFromGallery: async (mediaId: string) =>
        ensureOk(
          await browserApi.DELETE('/api/v1/admin/shops/{shopId}/gallery/{mediaId}', {
            params: { path: { shopId: mode.shopId, mediaId } },
          }),
        ),
      saveLocation: async (location: Schemas['ShopLocationRequest']) =>
        ensureOk(await browserApi.PUT('/api/v1/admin/shops/{shopId}/location', { ...path, body: location })),
    };
  }

  return {
    saveProfile: async (text, version) =>
      ensureOk(await browserApi.PUT('/api/v1/shop/profile', { body: { ...text, version } })),
    upload: async (slot: ImageSlot, file: File) =>
      ensureOk(
        slot === 'logo'
          ? await browserApi.PUT('/api/v1/shop/profile/logo', { body: form(file) })
          : await browserApi.PUT('/api/v1/shop/profile/cover', { body: form(file) }),
      ),
    remove: async (slot: ImageSlot) =>
      ensureOk(
        slot === 'logo'
          ? await browserApi.DELETE('/api/v1/shop/profile/logo')
          : await browserApi.DELETE('/api/v1/shop/profile/cover'),
      ),
    addToGallery: async (file: File) =>
      ensureOk(await browserApi.POST('/api/v1/shop/profile/gallery', { body: form(file) })),
    removeFromGallery: async (mediaId: string) =>
      ensureOk(
        await browserApi.DELETE('/api/v1/shop/profile/gallery/{mediaId}', { params: { path: { mediaId } } }),
      ),
    saveLocation: async (location: Schemas['ShopLocationRequest']) =>
      ensureOk(await browserApi.PUT('/api/v1/shop/location', { body: location })),
  };
}
