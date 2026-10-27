# Each base image is pinned by digest, which Dependabot keeps current (docs/dependabot.md#pins).
FROM nginxinc/nginx-unprivileged:1.28.0-alpine3.21@sha256:c97ff0bf7cbae369953c6da1232ec14ad9f971d66360c5698db0856a4cd657a0

COPY nginx.conf /etc/nginx/nginx.conf
COPY ./html/ /usr/share/nginx/html
