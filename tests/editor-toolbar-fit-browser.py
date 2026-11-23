"""Fit Compact tools to actual editor width, not viewport breakpoints."""
import asyncio
import runpy
from pathlib import Path
from playwright.async_api import async_playwright
ROOT = Path(__file__).resolve().parents[1]
f = runpy.run_path(str(ROOT / 'tests/editor-fullscreen-080-browser.py'))
s = runpy.run_path(str(ROOT / 'tests/editor-toolbar-split-080-browser.py'))
HTML = f['HTML'].replace('data-quick-command="ul">•</button>', 'data-quick-command="ul">•</button><button class="az-azunt-editor-button az-azunt-editor-quick-command" data-quick-command="ol">1.</button>').replace('data-quick-command="link">Link</button>', 'data-quick-command="link">Link</button><button class="az-azunt-editor-button az-azunt-editor-quick-command" data-quick-command="image">▧</button>' + ''.join(s['split'](name, True) for name in ('table','form','code')))

async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch(executable_path='/usr/bin/chromium', args=['--no-sandbox'])
        for touch in (False, True):
            page = await browser.new_page(viewport={'width':1280,'height':900}, has_touch=touch)
            errors=[]; page.on('pageerror', lambda err: errors.append(str(err)))
            await page.set_content('<dialog open style="position:relative;margin:0;padding:0;border:0;max-width:none;width:900px"><div id="host">'+HTML+'</div></dialog>')
            await page.add_style_tag(content=f['CSS']); await page.add_script_tag(content=f['JS'])
            root=page.locator('#browser-test'); quick=root.locator('[data-compact-quick]')
            await page.evaluate('AzuntEditor.setShowMenuBar("browser-test", false);AzuntEditor.setShowToolbarGroupLabels("browser-test",false)')
            async def measure(width):
                await page.locator('dialog').evaluate('(el,w)=>el.style.width=w+"px"',width)
                await page.wait_for_timeout(80)
                assert await root.locator('[data-azunt-menu="more"]').is_visible()
                assert await root.locator('[data-quick-command="fullscreen"]').is_visible()
                visible=await quick.evaluate('row=>Array.from(row.querySelectorAll("button")).filter(b=>b.getClientRects().length).map(b=>b.dataset.quickCommand||b.dataset.azuntMenu)')
                assert await quick.evaluate('row=>Array.from(row.querySelectorAll("button")).filter(b=>b.getClientRects().length).every(b=>b.getBoundingClientRect().right<=row.getBoundingClientRect().right+1)'), (width,visible)
                return visible
            wide=await measure(950)
            medium=await measure(710)
            small=await measure(490)
            tiny=await measure(280)
            assert 'ul' in medium and 'ol' in medium and 'link' in medium, medium
            assert 'ul' in small and 'bold' in small, small
            assert len(wide)>len(small)>=len(tiny), (wide,small,tiny)
            assert await measure(950)==wide
            await root.locator('[data-azunt-menu="more"]').click()
            assert await root.locator('[data-menu-panel] [data-group-label="Table"]').is_visible()
            assert not errors, errors
            print('PASS', 'touch' if touch else 'desktop', 'visible buttons at 950/710/490/280:',*[len(x) for x in (wide,medium,small,tiny)])
            await page.close()
        await browser.close()
if __name__=='__main__': asyncio.run(main())
