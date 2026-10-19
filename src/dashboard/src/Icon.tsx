const paths:Record<string,string>={
 overview:'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',
 devices:'M3 4h18v12H3z M8 21h8 M12 16v5',
 security:'M12 3 3 7v5c0 5 9 9 9 9s9-4 9-9V7z M8 12l3 3 5-6',
 support:'M4 5h16v12H9l-5 4z M8 9h8 M8 13h5',
 branding:'M12 3a9 9 0 1 0 0 18h2a2 2 0 0 0 0-4h-1a2 2 0 0 1 0-4h3a5 5 0 0 0 5-5c0-3-4-5-9-5 M7 9h.01 M10 6h.01 M15 6h.01',
 users:'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8 M22 21v-2a4 4 0 0 0-3-3.87 M16 3.13a4 4 0 0 1 0 7.75',
 audit:'M6 3h12v18H6z M9 7h6 M9 11h6 M9 15h4',
 logout:'M9 4H4v16h5 M14 8l4 4-4 4 M8 12h12',
 refresh:'M20 7v5h-5 M4 17v-5h5 M6 7a7 7 0 0 1 12-2l2 7 M18 17a7 7 0 0 1-12 2l-2-7',
 terminal:'M4 5h16v14H4z M7 9l3 3-3 3 M13 15h4',
 files:'M3 7V4h6l2 3h10v13H3z',
 download:'M12 3v12 M7 10l5 5 5-5 M4 16v5h16v-5',
 plus:'M12 5v14 M5 12h14',
 search:'M10.5 3a7.5 7.5 0 1 0 0 15 7.5 7.5 0 0 0 0-15 M16 16l5 5',
 arrow:'M5 12h14 M13 6l6 6-6 6',
 settings:'M4 7h16 M4 17h16 M8 4v6 M16 14v6',
 cpu:'M7 7h10v10H7z M4 9h3 M4 15h3 M17 9h3 M17 15h3 M9 4v3 M15 4v3 M9 17v3 M15 17v3',
 clock:'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18 M12 7v5l3 2',
 close:'M6 6l12 12 M6 18 18 6',
 check:'M5 12l4 4L19 6',
};
export function Icon({name,size=19}:{name:string;size?:number}){
 return <svg className="icon" width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false"><path d={paths[name]??paths.devices}/></svg>;
}
