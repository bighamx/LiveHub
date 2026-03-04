import fs from 'fs';

function replaceStyle(vueFile, scssFile) {
    let vueContent = fs.readFileSync(vueFile, 'utf8');
    let scssContent = fs.readFileSync(scssFile, 'utf8');

    // regex to replace <style scoped>...</style>
    const newStyle = '<style scoped lang="scss">\n' + scssContent + '\n</style>';
    const updated = vueContent.replace(/<style scoped>[\s\S]*?<\/style>/m, newStyle);

    fs.writeFileSync(vueFile, updated);
}

replaceStyle('./src/components/VideoPlayer.vue', './src/videoplayer.scss.tmp');
replaceStyle('./src/App.vue', './src/app.scss.tmp');

fs.unlinkSync('./src/videoplayer.scss.tmp');
fs.unlinkSync('./src/app.scss.tmp');
