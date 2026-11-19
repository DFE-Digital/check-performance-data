# Each base image is pinned by digest, which Dependabot keeps current (docs/dependabot.md#pins).
FROM nginx/nginx-unprivileged:1.30.7-alpine3.24

COPY nginx.conf /etc/nginx/nginx.conf
COPY ./html/ /usr/share/nginx/html
