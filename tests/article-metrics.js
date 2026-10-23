(() => {
    const dpr = devicePixelRatio;
    const box = element => {
        const r = element.getBoundingClientRect();
        return {x:r.x*dpr, y:(r.y+scrollY)*dpr, width:r.width*dpr, height:r.height*dpr};
    };
    return {
        bottom: Math.abs(document.documentElement.scrollHeight-innerHeight-scrollY)<2,
        dpr, height:document.documentElement.scrollHeight, viewport:innerHeight, scrollY,
        paragraphs:[...document.querySelectorAll('#text p')].map(box), end:box(document.querySelector('#end'))
    };
})();
