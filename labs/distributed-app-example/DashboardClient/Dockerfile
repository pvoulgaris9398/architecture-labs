FROM node:22-alpine AS build
WORKDIR /app
ARG VITE_API_BASE_URL
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
COPY package*.json ./
RUN npm install
COPY . .
RUN test -n "$VITE_API_BASE_URL" || (echo "VITE_API_BASE_URL is required" >&2; exit 1)
RUN npm run build

# Light Nginx instance to serve production static files
FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
