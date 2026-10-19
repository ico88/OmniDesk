import { useEffect, useState } from 'react';
import { api } from './api';
import { Icon } from './Icon';
import './knowledge.css';

type Article = { id:string; slug:string; title:string; summary:string; body?:string; isPublished:boolean; siteId:string|null; groupId:string|null; version:number; updatedAt:string; publishedAt:string|null };
type Revision = { version:number; slug:string; title:string; summary:string; isPublished:boolean; siteId:string|null; groupId:string|null; publishedAt:string|null; recordedAt:string; recordedBy:string|null };
type HistoricalArticle = Omit<Article,'id'|'updatedAt'> & { articleId:string; recordedAt:string; recordedBy:string|null };
type ScopeOption = { id:string; name:string; isArchived?:boolean };
const blank = { slug:'', title:'', summary:'', body:'', isPublished:false, scope:'global' };

export function KnowledgeBase({ canManage, onMessage }: { canManage:boolean; onMessage:(message:string)=>void }) {
  const [articles,setArticles]=useState<Article[]>([]);
  const [selected,setSelected]=useState<Article|null>(null);
  const [versions,setVersions]=useState<Revision[]>([]);
  const [historical,setHistorical]=useState<HistoricalArticle|null>(null);
  const [draft,setDraft]=useState(blank);
  const [editing,setEditing]=useState(false);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  const [search,setSearch]=useState('');
  const [sites,setSites]=useState<ScopeOption[]>([]);
  const [groups,setGroups]=useState<ScopeOption[]>([]);

  function scopeName(siteId:string|null,groupId:string|null) {
    if(siteId){const site=sites.find(x=>x.id===siteId);return site?`Sede · ${site.name}`:'Sede';}
    if(groupId){const group=groups.find(x=>x.id===groupId);return group?`Gruppo · ${group.name}`:'Gruppo';}
    return 'Globale';
  }

  async function openArticle(id:string) {
    const full=await api<Article>(`/knowledge/${id}`);
    setSelected(full);setDraft({slug:full.slug,title:full.title,summary:full.summary,body:full.body??'',isPublished:full.isPublished,scope:full.siteId?`site:${full.siteId}`:full.groupId?`group:${full.groupId}`:'global'});setEditing(false);setHistorical(null);
    setVersions(canManage?await api<Revision[]>(`/knowledge/${id}/versions`):[]);
  }

  async function load(preferredId?:string) {
    const list=await api<Article[]>('/knowledge');
    setArticles(list);
    const next=list.find(item=>item.id===(preferredId??selected?.id))??list[0]??null;
    if(next)await openArticle(next.id);
    else {setSelected(null);setVersions([]);setHistorical(null);setDraft(blank);}
  }
  useEffect(()=>{let active=true;if(canManage)void api<{sites:ScopeOption[];groups:ScopeOption[]}>('/knowledge/scopes').then(scopes=>{if(active){setSites(scopes.sites);setGroups(scopes.groups);}}).catch(e=>{if(active)setError(e instanceof Error?e.message:'Impossibile caricare sedi e gruppi');});void api<Article[]>('/knowledge').then(async list=>{
    if(!active)return;setArticles(list);
    const first=list[0];if(first){const full=await api<Article>(`/knowledge/${first.id}`);const history=canManage?await api<Revision[]>(`/knowledge/${first.id}/versions`):[];if(active){setSelected(full);setDraft({slug:full.slug,title:full.title,summary:full.summary,body:full.body??'',isPublished:full.isPublished,scope:full.siteId?`site:${full.siteId}`:full.groupId?`group:${full.groupId}`:'global'});setVersions(history);}}
  }).catch(e=>{if(active)setError(e instanceof Error?e.message:'La knowledge base non è disponibile');});return()=>{active=false;};},[canManage]);
  async function work(run:()=>Promise<void>){setBusy(true);setError('');try{await run();}catch(e){setError(e instanceof Error?e.message:'Operazione non riuscita');}finally{setBusy(false);}}
  const visible=articles.filter(article=>`${article.title} ${article.summary} ${article.slug}`.toLocaleLowerCase().includes(search.toLocaleLowerCase()));

  return <div className="knowledge-layout">
    <section className="panel knowledge-list">
      <div className="section-heading"><div><span className="eyebrow">GUIDE OPERATIVE</span><h2>Knowledge base <span className="count">{articles.length}</span></h2></div>{canManage&&<button onClick={()=>{setSelected(null);setDraft(blank);setEditing(true);}}><Icon name="plus"/>Nuovo</button>}</div>
      <label className="search-field"><Icon name="search"/><input aria-label="Cerca articoli" placeholder="Cerca una guida…" value={search} onChange={e=>setSearch(e.target.value)}/></label>
      {visible.length===0?<p className="empty-state">{articles.length?'Nessun articolo corrisponde alla ricerca.':'Non ci sono guide pubblicate.'}</p>:<div className="knowledge-article-list">{visible.map(article=><button key={article.id} className={`knowledge-article-choice ${selected?.id===article.id&&!editing?'selected':''}`} onClick={()=>void work(()=>openArticle(article.id))}><strong>{article.title}</strong><small>{scopeName(article.siteId,article.groupId)} · {article.summary||article.slug}</small>{canManage&&!article.isPublished&&<span className="status-pill offline">Bozza</span>}</button>)}</div>}
    </section>
    <section className="panel knowledge-detail">
      {error&&<p role="alert" className="error">{error}</p>}
      {editing&&canManage?<form className="knowledge-editor" onSubmit={e=>{e.preventDefault();void work(async()=>{
        const [scopeType,scopeId]=draft.scope.split(':');
        const payload={slug:draft.slug,title:draft.title,summary:draft.summary,body:draft.body,isPublished:draft.isPublished,siteId:scopeType==='site'?scopeId:null,groupId:scopeType==='group'?scopeId:null};
        const saved=selected
          ?await api<Article>(`/knowledge/${selected.id}`,'PUT',{...payload,scopeSpecified:true,expectedVersion:selected.version})
          :await api<Article>('/knowledge','POST',payload);
        await load(saved.id);setEditing(false);onMessage(selected?'Articolo aggiornato':'Articolo creato');
      });}}><span className="eyebrow">{selected?'MODIFICA GUIDA':'NUOVA GUIDA'}</span><label>Ambito<select aria-label="Ambito articolo" value={draft.scope} onChange={e=>setDraft({...draft,scope:e.target.value})} disabled={busy}><option value="global">Globale</option>{sites.map(site=><option key={site.id} value={`site:${site.id}`}>Sede · {site.name}</option>)}{groups.map(group=><option key={group.id} value={`group:${group.id}`}>Gruppo · {group.name}</option>)}</select></label><label>Slug<input required maxLength={120} pattern="[a-z0-9]+(-[a-z0-9]+)*" placeholder="esempio-guida" value={draft.slug} onChange={e=>setDraft({...draft,slug:e.target.value})} disabled={busy}/></label><label>Titolo<input required maxLength={160} value={draft.title} onChange={e=>setDraft({...draft,title:e.target.value})} disabled={busy}/></label><label>Descrizione breve<input maxLength={500} value={draft.summary} onChange={e=>setDraft({...draft,summary:e.target.value})} disabled={busy}/></label><label>Contenuto<textarea required maxLength={30000} rows={18} value={draft.body} onChange={e=>setDraft({...draft,body:e.target.value})} disabled={busy}/></label><label className="knowledge-publish"><input type="checkbox" checked={draft.isPublished} onChange={e=>setDraft({...draft,isPublished:e.target.checked})} disabled={busy}/>Pubblica agli operatori</label><div className="action-row"><button disabled={busy}>{selected?'Salva modifiche':'Crea articolo'}</button><button type="button" className="secondary" disabled={busy} onClick={()=>{setEditing(false);if(selected)void work(()=>load(selected.id));}}>Annulla</button></div><small>Gli articoli scoped sono visibili agli utenti autorizzati alla relativa sede o gruppo. Il contenuto viene mostrato come testo, senza eseguire HTML.</small></form>
        :selected?<article className="knowledge-content">
          {historical?<div className="knowledge-version-preview"><div className="section-heading"><div><span className="eyebrow">STORICO · VERSIONE {historical.version}</span><h2>{historical.title}</h2></div><button className="secondary compact" onClick={()=>setHistorical(null)}>Versione corrente</button></div><p className="knowledge-summary">{historical.summary}</p><small>{scopeName(historical.siteId,historical.groupId)} · Registrata {new Date(historical.recordedAt).toLocaleString('it-IT')}{historical.recordedBy?` · ${historical.recordedBy}`:''}</small><div className="knowledge-body">{historical.body}</div></div>
            :<><div className="section-heading"><div><span className="eyebrow">{selected.isPublished?'GUIDA PUBBLICATA':'BOZZA'}</span><h2>{selected.title}</h2></div>{canManage&&<button className="secondary compact" onClick={()=>{setHistorical(null);setEditing(true);}}><Icon name="settings"/>Modifica</button>}</div><p className="knowledge-summary">{selected.summary}</p><small>{scopeName(selected.siteId,selected.groupId)} · Ultimo aggiornamento {new Date(selected.updatedAt).toLocaleString('it-IT')}</small><div className="knowledge-body">{selected.body}</div></>}
          {canManage&&<details className="knowledge-history"><summary>Cronologia versioni <span className="count">{versions.length}</span></summary>{versions.length===0?<p>Nessuna revisione disponibile.</p>:<div className="knowledge-version-list">{versions.map(version=><button key={version.version} type="button" className="secondary" aria-label={`Apri versione ${version.version}: ${version.title}`} disabled={busy} onClick={()=>void work(async()=>setHistorical(await api<HistoricalArticle>(`/knowledge/${selected.id}/versions/${version.version}`)))}><strong>v{version.version} · {version.title}</strong><small>{new Date(version.recordedAt).toLocaleString('it-IT')} · {version.isPublished?'Pubblicata':'Bozza'}{version.recordedBy?` · ${version.recordedBy}`:''}</small></button>)}</div>}</details>}
        </article>
        :<div className="empty-state"><Icon name="support" size={34}/><h3>Guide per gli interventi</h3><p>Seleziona un articolo{canManage?' o crea la prima guida':''}.</p></div>}
    </section>
  </div>;
}
