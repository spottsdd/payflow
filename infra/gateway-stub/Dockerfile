FROM node:24-slim

WORKDIR /app
COPY package.json ./
COPY index.js ./

ENV PORT=9999
ENV STUB_FAILURE_RATE=0.1

EXPOSE 9999
CMD ["node", "index.js"]
