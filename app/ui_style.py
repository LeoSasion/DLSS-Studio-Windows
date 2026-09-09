import gradio as gr

THEME = gr.themes.Soft(primary_hue="teal", secondary_hue="slate", neutral_hue="slate", font=["Microsoft YaHei", "PingFang SC", "sans-serif"])
CSS = """
.gradio-container {max-width:1320px!important; margin:auto!important; padding:clamp(16px,3vw,32px) clamp(12px,3vw,36px)!important;}
.gradio-container .main {padding:0!important;}
.html-container {padding:0!important;}
body {background:var(--body-background-fill);}
.studio-header {display:flex;justify-content:space-between;align-items:center;gap:24px;padding:12px 0 24px;}
.eyebrow {font-size:11px;letter-spacing:2px;color:var(--body-text-color-subdued);font-weight:700;}
.studio-header h1 {font-size:38px!important;letter-spacing:-1px;line-height:1.3;margin:14px 0 12px!important;font-weight:750;}
.studio-header p {font-size:14px;color:var(--body-text-color-subdued);line-height:1.8;}
.local-badge {white-space:nowrap;border:1px solid var(--border-color-primary);border-radius:30px;padding:9px 15px;font-size:12px;color:var(--color-accent);}
.workflow {padding:0!important;margin-bottom:12px!important;}
.workflow p {font-size:13px!important;word-spacing:8px;color:var(--body-text-color-subdued);}
.tab-nav {gap:8px!important;margin-bottom:22px!important;padding-bottom:10px!important;}
.tab-nav button {padding:11px 22px!important;font-weight:650!important;border-radius:8px!important;}
.tab-nav button.selected {background:var(--button-primary-background-fill)!important;color:var(--button-primary-text-color)!important;}
.input-panel, .result-panel {gap:18px!important;}
.result-panel {padding:20px!important;border:1px solid var(--border-color-primary);border-radius:14px;background:var(--block-background-fill);}
.gradio-container h3 {font-size:15px!important;letter-spacing:.5px;margin:0!important;}
.helper p, .studio-footer p {color:var(--body-text-color-subdued);font-size:12px!important;line-height:1.8;}
.studio-footer {border-top:1px solid var(--border-color-primary);margin-top:24px!important;padding-top:18px!important;}
button.primary {min-height:48px!important;box-shadow:none!important;font-weight:700!important;}
footer {display:none!important;}
@media(min-width:1000px){.result-panel{position:sticky;top:20px;}}
@media(max-width:700px){.gradio-container{padding:16px 12px!important}.studio-header{align-items:flex-start;gap:8px}.studio-header h1{font-size:27px!important}.local-badge{padding:6px 9px;font-size:10px}.studio-header p{font-size:12px}.workflow p{word-spacing:0}.result-panel{padding:12px!important}.tab-nav button{padding:10px 16px!important}}
"""
