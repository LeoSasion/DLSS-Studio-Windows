import os
os.environ['GRADIO_ANALYTICS_ENABLED'] = 'False'
import uvicorn
from studio_server import api

if __name__ == '__main__':
    uvicorn.run(api, host='127.0.0.1', port=int(os.environ.get('DLSS_STUDIO_PORT', '7860')))
