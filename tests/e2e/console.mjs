// Interactions with the console that more than one browser spec needs, written the way a person uses it.

// Searches the last 24 hours for one source address. The page already searches that range when it opens, so the range is not pressed again
// (each search that spans several pages keeps one of the two snapshots a user may hold for ten minutes).
export async function searchByIp(page, sourceIp) {
  if ((await page.locator('#sourceIp').count()) === 0) {
    await page.getByRole('button', { name: /Filtro/ }).click();
    await page.getByRole('menuitem', { name: /IP origen/ }).click();
  }
  await page.locator('#sourceIp').fill(sourceIp);
  await page.getByRole('button', { name: 'Buscar sesiones' }).click();
}
